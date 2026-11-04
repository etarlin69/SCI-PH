using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Patient;
using SCI_PH.Mobile.Features.Patient.Services;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;
using SCI_PH.Mobile.Features.Patient.ViewModels;
using SCI_PH.Mobile.Tests.Auth;

namespace SCI_PH.Mobile.Tests.Patient;

// Shell conserva páginas y ViewModels: nada del paciente A puede verse después de su logout ni con el paciente B, y una
// respuesta que llega tarde no vuelve a ponerlo en pantalla.
public class PatientAccountIsolationTests
{
    private readonly FakePatientSelfBff _bff = new();
    private readonly ClinicalSessionService _session = PatientSession.SignedIn();

    private sealed record Screens(HomeViewModel Home, HistoryViewModel History, ResultsViewModel Results, ProfileViewModel Profile);

    private Screens RetainedScreens()
    {
        var data = new HttpPatientDataService(_bff.Client());
        return new Screens(
            new HomeViewModel(data, _session), new HistoryViewModel(data, _session), new ResultsViewModel(data, _session), new ProfileViewModel(data, _session));
    }

    private static async Task LoadAllAsync(Screens screens)
    {
        await screens.Home.LoadCommand.ExecuteAsync(null);
        await screens.History.LoadCommand.ExecuteAsync(null);
        await screens.Results.LoadCommand.ExecuteAsync(null);
        await screens.Profile.LoadCommand.ExecuteAsync(null);
    }

    private static void AssertEmpty(Screens screens)
    {
        Assert.Null(screens.Home.Summary);
        Assert.Null(screens.History.History);
        Assert.Empty(screens.Results.Results);
        Assert.Null(screens.Profile.Profile);
    }

    [Fact]
    public async Task ClosingTheSessionClearsEveryRetainedScreenAtOnce()
    {
        var screens = RetainedScreens();
        await LoadAllAsync(screens);
        Assert.NotNull(screens.Home.Summary);
        Assert.NotEmpty(screens.Results.Results);
        Assert.NotNull(screens.Profile.Profile);

        _session.Clear();

        AssertEmpty(screens);
    }

    [Fact]
    public async Task PatientBNeverSeesPatientAsProfileHistoryResultsOrSummary()
    {
        var screens = RetainedScreens();
        await LoadAllAsync(screens);
        Assert.Equal("Ana Pérez", screens.Home.Summary!.DisplayName);

        _session.Clear();
        PatientSession.SignIn(_session, Guid.NewGuid());
        AssertEmpty(screens);

        const string me = FakePatientSelfBff.Me;
        _bff.Responses[me] = """{"givenNames":"Berta","familyNames":"Gómez","dateOfBirth":null,"sex":"Female","document":null,"email":null,"phone":null}""";
        _bff.Responses[$"{me}/summary"] =
            """{"displayName":"Berta Gómez","encounterCount":0,"resultCount":0,"latestEncounter":null,"latestResult":null,"activeAllergies":[]}""";
        foreach (var route in new[] { "encounters", "allergies", "diagnoses", "prescriptions", "results" })
        {
            _bff.Responses[$"{me}/{route}"] = "[]";
        }

        await LoadAllAsync(screens);

        Assert.Equal("Berta Gómez", screens.Home.Summary!.DisplayName);
        Assert.Null(screens.Home.Summary.LatestResult);
        Assert.Equal("Berta Gómez", screens.Profile.Profile!.DisplayName);
        Assert.Empty(screens.History.History!.Allergies);
        Assert.Empty(screens.History.History.Diagnoses);
        Assert.Empty(screens.Results.Results);
    }

    [Fact]
    public async Task ALateResponseForPatientAIsDiscardedAfterPatientBSignsIn()
    {
        var screens = RetainedScreens();
        var gate = new TaskCompletionSource();
        _bff.Gate = gate;
        // Las cuatro peticiones salen con la sesión de A y siguen en vuelo cuando esa sesión termina.
        var loading = Task.WhenAll(
            screens.Home.LoadCommand.ExecuteAsync(null),
            screens.History.LoadCommand.ExecuteAsync(null),
            screens.Results.LoadCommand.ExecuteAsync(null),
            screens.Profile.LoadCommand.ExecuteAsync(null));

        _session.Clear();
        PatientSession.SignIn(_session, Guid.NewGuid());
        gate.SetResult();
        await loading;

        AssertEmpty(screens);
        Assert.False(screens.Home.HasError);
    }

    [Fact]
    public async Task ALateFailureForPatientAIsNotShownToPatientB()
    {
        var screens = RetainedScreens();
        var gate = new TaskCompletionSource();
        _bff.Gate = gate;
        _bff.Status = HttpStatusCode.ServiceUnavailable;
        var loading = screens.Home.LoadCommand.ExecuteAsync(null);

        _session.Clear();
        PatientSession.SignIn(_session, Guid.NewGuid());
        gate.SetResult();
        await loading;

        Assert.False(screens.Home.HasError);
        Assert.Null(screens.Home.Summary);
    }

    [Fact]
    public async Task RenewingTheTokenOfTheSameAccountKeepsTheScreens()
    {
        var screens = RetainedScreens();
        await LoadAllAsync(screens);

        _session.SetAuthentication("renewed-access-token", DateTimeOffset.UtcNow.AddMinutes(15));

        Assert.NotNull(screens.Home.Summary);
        Assert.NotEmpty(screens.Results.Results);
    }

    [Fact]
    public async Task AGeneric503LeavesEveryScreenInItsErrorStateWithoutStaleOrFakeData()
    {
        var screens = RetainedScreens();
        await LoadAllAsync(screens);

        _bff.Status = HttpStatusCode.ServiceUnavailable;
        await LoadAllAsync(screens);

        AssertEmpty(screens);
        Assert.Equal(MobileApiErrorMessages.Server, screens.Home.ErrorMessage);
        Assert.Equal(MobileApiErrorMessages.Server, screens.History.ErrorMessage);
        Assert.Equal(MobileApiErrorMessages.Server, screens.Results.ErrorMessage);
        Assert.Equal(MobileApiErrorMessages.Server, screens.Profile.ErrorMessage);
    }

    [Fact]
    public async Task RealLogoutRevokesOnTheServerClearsTheDeviceAndEmptiesTheScreens()
    {
        using var auth = new AuthHarness();
        auth.Bff.Roles = [AccountRoles.Patient];
        auth.Bff.Permissions = [];
        await auth.SignInAsync();
        Assert.Equal(AccountArea.Patient, auth.Session.Area);

        var data = new HttpPatientDataService(_bff.Client());
        var home = new HomeViewModel(data, auth.Session);
        await home.LoadCommand.ExecuteAsync(null);
        Assert.NotNull(home.Summary);

        await auth.Auth.LogoutAsync();

        Assert.Equal(1, auth.Bff.LogoutCalls);
        Assert.Empty(auth.Storage.Values);
        Assert.Null(auth.Session.AccessToken);
        Assert.Null(auth.Session.CurrentUser);
        Assert.True(auth.LocalData.ClearCalls >= 1);
        Assert.Null(home.Summary);
    }

    [Fact]
    public async Task LegacyLocalDataIsCleanedAtStartupEvenWithAnOpenSession()
    {
        using var auth = new AuthHarness();

        await auth.Auth.RestoreSessionAsync();

        Assert.Equal(1, auth.LocalData.ClearCalls);
    }
}

public class LegacyProfileDataCleanerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("sciph-legacy-profile-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void TheOldDemoKeysAndPhotoAreRemovedAndNothingElse()
    {
        var preferences = new FakePreferences();
        preferences.Set(LegacyProfileDataCleaner.DescriptionKey, "Texto del paciente A");
        preferences.Set(LegacyProfileDataCleaner.PhotoKey, Path.Combine(_directory, "patient_profile_photo.jpg"));
        preferences.Set("other_setting", "kept");
        File.WriteAllText(Path.Combine(_directory, "patient_profile_photo.jpg"), "photo");
        File.WriteAllText(Path.Combine(_directory, "patient_profile_photo.png"), "photo");
        File.WriteAllText(Path.Combine(_directory, "other.jpg"), "kept");

        new LegacyProfileDataCleaner(preferences, _directory).Clear();

        Assert.False(preferences.ContainsKey(LegacyProfileDataCleaner.DescriptionKey));
        Assert.False(preferences.ContainsKey(LegacyProfileDataCleaner.PhotoKey));
        Assert.True(preferences.ContainsKey("other_setting"));
        Assert.Equal(["other.jpg"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void CleaningNeverThrowsSoItCannotBlockALogout()
    {
        var cleaner = new LegacyProfileDataCleaner(new FakePreferences { FailRemovals = true }, Path.Combine(_directory, "missing"));

        cleaner.Clear();
    }

    private sealed class FakePreferences : IPreferences
    {
        private readonly Dictionary<string, object?> _values = [];

        public bool FailRemovals { get; init; }

        public bool ContainsKey(string key, string? sharedName = null) => _values.ContainsKey(key);

        public void Remove(string key, string? sharedName = null)
        {
            if (FailRemovals)
            {
                throw new InvalidOperationException("Unavailable preferences (test).");
            }

            _values.Remove(key);
        }

        public void Clear(string? sharedName = null) => _values.Clear();

        public void Set<T>(string key, T value, string? sharedName = null) => _values[key] = value;

        public T Get<T>(string key, T defaultValue, string? sharedName = null) =>
            _values.TryGetValue(key, out var value) ? (T)value! : defaultValue;
    }
}

// El área del paciente no tiene datos simulados ni datos locales, y Android no respalda los datos de la app.
public class PatientAppSurfaceTests
{
    private static string AppFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "SCI-PH.Mobile", relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(relativePath);
    }

    [Fact]
    public void TheAppHasNoSimulatedPatientServiceOrScreen()
    {
        var types = typeof(HttpPatientDataService).Assembly.GetTypes();

        Assert.DoesNotContain(types, t =>
            t.Namespace?.StartsWith("SCI_PH.Mobile.Features.Patient", StringComparison.Ordinal) == true
            && (t.Name.Contains("Mock", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Fake", StringComparison.OrdinalIgnoreCase)));
        foreach (var removed in new[] { "QrScanPage", "FaceVerifyPage", "AiAssistantPage", "MockPatientSessionService", "MockPatientDataService", "MockPatientAssistantService" })
        {
            Assert.DoesNotContain(types, t => t.Name == removed);
        }
    }

    [Fact]
    public void ThePatientFeatureRegistersOnlyTheRealDataService()
    {
        var services = new ServiceCollection().AddPatientFeature();

        var data = Assert.Single(services, d => d.ServiceType == typeof(IPatientDataService));
        Assert.Equal(typeof(HttpPatientDataService), data.ImplementationType);
    }

    [Fact]
    public void AndroidDoesNotBackUpTheAppNorAskForTheGallery()
    {
        var manifest = File.ReadAllText(AppFile(Path.Combine("Platforms", "Android", "AndroidManifest.xml")));

        Assert.Contains("android:allowBackup=\"false\"", manifest);
        Assert.DoesNotContain("android:allowBackup=\"true\"", manifest);
        Assert.DoesNotContain("READ_EXTERNAL_STORAGE", manifest);
        Assert.DoesNotContain("READ_MEDIA_IMAGES", manifest);
    }

    [Fact]
    public void ProfileScreenIsReadOnly()
    {
        var page = File.ReadAllText(AppFile(Path.Combine("Features", "Patient", "Profile", "ProfilePage.xaml")));

        foreach (var removed in new[] { "PhotoSource", "ChangePhotoCommand", "DescriptionDraft", "SaveDescriptionCommand", "<Editor", "<Button" })
        {
            Assert.DoesNotContain(removed, page);
        }
    }
}
