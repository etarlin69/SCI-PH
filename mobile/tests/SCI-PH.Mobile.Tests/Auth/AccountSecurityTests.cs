using System.Net;
using System.Text;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Features.Auth;
using SCI_PH.Mobile.Features.Auth.Services;
using SCI_PH.Mobile.Features.Auth.ViewModels;

namespace SCI_PH.Mobile.Tests.Auth;

// "Seguridad de la cuenta": cambiar la contraseña y cerrar todas las sesiones con los contratos existentes del Mobile BFF
// (POST auth/change-password, auth/revoke-sessions). El servidor revoca todos los refresh tokens, así que tras el éxito
// la sesión local termina y se vuelve a la entrada. Las contraseñas nunca quedan en el formulario.
public class AccountSecurityTests
{
    private const string StrongPassword = "Nueva-Clave-2026";

    private sealed class RecordingBff : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.NoContent;

        public MobileApiClient Client() => new(new HttpClient(this) { BaseAddress = new Uri("http://sciph.test/") });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return Status == HttpStatusCode.NoContent
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new HttpResponseMessage(Status) { Content = new StringContent("""{"title":"x"}""", Encoding.UTF8, "application/problem+json") };
        }
    }

    private sealed class ScriptedSecurity : IAccountSecurityService
    {
        public List<(string Current, string Next)> Changes { get; } = [];

        public int Revocations { get; private set; }

        public Exception? Failure { get; set; }

        public Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
        {
            Changes.Add((currentPassword, newPassword));
            return Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }

        public Task RevokeAllSessionsAsync(CancellationToken cancellationToken = default)
        {
            Revocations++;
            return Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }
    }

    private readonly ScriptedSecurity _security = new();
    private readonly FakeNavigator _navigator = new();
    private readonly FakeDialogs _dialogs = new();

    private AccountSecurityViewModel ViewModel(string current = "Actual-Clave-1", string next = StrongPassword, string? confirm = null) =>
        new(_security, _navigator, _dialogs) { CurrentPassword = current, NewPassword = next, ConfirmPassword = confirm ?? next };

    [Fact]
    public async Task TheServiceUsesTheExistingContractsAndThenEndsTheLocalSession()
    {
        var bff = new RecordingBff();
        var auth = new FakeAuthService();
        var service = new AccountSecurityService(bff.Client(), auth);

        await service.ChangePasswordAsync("Actual-Clave-1", StrongPassword);
        await service.RevokeAllSessionsAsync();

        Assert.Equal(
            [(HttpMethod.Post, "/api/mobile/auth/change-password"), (HttpMethod.Post, "/api/mobile/auth/revoke-sessions")],
            bff.Requests.Select(r => (r.Method, r.Path)));
        using var body = JsonDocument.Parse(bff.Requests[0].Body!);
        Assert.Equal(["currentPassword", "newPassword"], body.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, auth.LogoutCalls);
    }

    [Fact]
    public async Task ARejectedChangeKeepsTheSessionOpen()
    {
        var bff = new RecordingBff { Status = HttpStatusCode.BadRequest };
        var auth = new FakeAuthService();

        await Assert.ThrowsAsync<MobileApiException>(() => new AccountSecurityService(bff.Client(), auth).ChangePasswordAsync("mala", StrongPassword));

        Assert.Equal(0, auth.LogoutCalls);
    }

    [Fact]
    public async Task ASuccessfulChangeSaysWhyAndGoesBackToTheEntry()
    {
        var vm = ViewModel();

        await vm.ChangePasswordCommand.ExecuteAsync(null);

        Assert.Equal(("Actual-Clave-1", StrongPassword), Assert.Single(_security.Changes));
        Assert.Equal(AccountSecurityViewModel.PasswordChanged, Assert.Single(_dialogs.Shown).Message);
        Assert.Equal(AppRoutes.Root(AppRoutes.Entry), Assert.Single(_navigator.Navigations).Route);
        Assert.Equal((string.Empty, string.Empty, string.Empty), (vm.CurrentPassword, vm.NewPassword, vm.ConfirmPassword));
        Assert.False(vm.HasError);
    }

    [Theory]
    [InlineData("", StrongPassword, StrongPassword, AccountSecurityViewModel.RequiredFields)]
    [InlineData("Actual-Clave-1", StrongPassword, "Otra-Clave-2026", AccountSecurityViewModel.Mismatch)]
    [InlineData(StrongPassword, StrongPassword, StrongPassword, AccountSecurityViewModel.SameAsCurrent)]
    [InlineData("Actual-Clave-1", "corta1!A", "corta1!A", AccountSecurityViewModel.WeakPassword)]
    [InlineData("Actual-Clave-1", "sinmayusculas-2026", "sinmayusculas-2026", AccountSecurityViewModel.WeakPassword)]
    [InlineData("Actual-Clave-1", "SinSimbolo2026", "SinSimbolo2026", AccountSecurityViewModel.WeakPassword)]
    public async Task AnInvalidFormIsNotSent(string current, string next, string confirm, string message)
    {
        var vm = ViewModel(current, next, confirm);

        await vm.ChangePasswordCommand.ExecuteAsync(null);

        Assert.Equal(message, vm.ErrorMessage);
        Assert.Empty(_security.Changes);
        Assert.Empty(_navigator.Navigations);
    }

    [Theory]
    [InlineData(MobileApiError.BadRequest, AccountSecurityViewModel.Rejected)]
    [InlineData(MobileApiError.Network, null)]
    public async Task ARejectedOrFailedChangeStaysOnThePageWithoutThePasswords(MobileApiError error, string? message)
    {
        _security.Failure = new MobileApiException(error, error == MobileApiError.BadRequest ? HttpStatusCode.BadRequest : null);
        var vm = ViewModel();

        await vm.ChangePasswordCommand.ExecuteAsync(null);

        Assert.Equal(message ?? MobileApiErrorMessages.For((MobileApiException)_security.Failure), vm.ErrorMessage);
        Assert.Empty(_navigator.Navigations);
        Assert.Empty(_dialogs.Shown);
        Assert.Equal((string.Empty, string.Empty, string.Empty), (vm.CurrentPassword, vm.NewPassword, vm.ConfirmPassword));
    }

    [Fact]
    public async Task ClosingEverySessionEndsThisOneToo()
    {
        var vm = ViewModel();

        await vm.RevokeAllSessionsCommand.ExecuteAsync(null);

        Assert.Equal(1, _security.Revocations);
        Assert.Equal(AccountSecurityViewModel.SessionsRevoked, Assert.Single(_dialogs.Shown).Message);
        Assert.Equal(AppRoutes.Root(AppRoutes.Entry), Assert.Single(_navigator.Navigations).Route);
    }

    [Fact]
    public async Task BothAreasReachTheScreen()
    {
        var account = new ClinicalAccountViewModel(new FakeAuthService(), new SCI_PH.Mobile.Common.Authentication.ClinicalSessionService(), _navigator);

        await account.OpenAccountSecurityCommand.ExecuteAsync(null);

        Assert.Equal(AuthRoutes.AccountSecurity, Assert.Single(_navigator.Navigations).Route);
        var shell = File.ReadAllText(Path.Combine(MobileSourceRoot(), "AppShell.xaml.cs"));
        Assert.Contains("GoToAsync(AuthRoutes.AccountSecurity)", shell);
    }

    private static string MobileSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "SCI-PH.Mobile", "AppShell.xaml.cs");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }
        }

        throw new DirectoryNotFoundException("SCI-PH.Mobile");
    }
}
