using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Authentication.Contracts;

namespace SCI_PH.Mobile.Tests.Auth;

/// <summary>
/// La infraestructura real de autenticación (cliente público y protegido, handler, servicio, sesión y almacén sobre
/// <see cref="ISecureStorage"/>) contra <see cref="FakeMobileBff"/>: sin red ni MAUI.
/// </summary>
internal sealed class AuthHarness : IDisposable
{
    private static readonly MobileApiOptions Options = new() { BaseAddress = new Uri("http://sciph.test") };

    private readonly HttpClient _publicHttp;
    private readonly HttpClient _protectedHttp;

    public AuthHarness()
    {
        Session = new ClinicalSessionService();
        Storage = new FakeSecureStorage();
        Tokens = new MauiSecureTokenStore(Storage);

        _publicHttp = Client(null);
        Auth = new MobileAuthService(new MobileAuthApi(new MobileApiClient(_publicHttp)), Session, Tokens);
        Auth.SessionExpired += (_, _) => SessionExpiredCount++;

        _protectedHttp = Client(new AuthenticatedHttpHandler(Session, Auth));
        Protected = new MobileApiClient(_protectedHttp);
    }

    public FakeMobileBff Bff { get; } = new();

    public ClinicalSessionService Session { get; }

    public FakeSecureStorage Storage { get; }

    public MauiSecureTokenStore Tokens { get; }

    public MobileAuthService Auth { get; }

    /// <summary>Cliente de endpoints protegidos (con <see cref="AuthenticatedHttpHandler"/>).</summary>
    public MobileApiClient Protected { get; }

    public int SessionExpiredCount { get; private set; }

    public Task<SignedIn> GetProtectedAsync() =>
        Protected.GetAsync<SignedIn>(FakeMobileBff.ProtectedPath.TrimStart('/'));

    public async Task SignInAsync()
    {
        var result = await Auth.LoginAsync(FakeMobileBff.Identifier, FakeMobileBff.Password);
        Assert.Equal(ClinicalSignInStatus.Authenticated, result.Status);
    }

    public void Dispose()
    {
        _publicHttp.Dispose();
        _protectedHttp.Dispose();
    }

    // El handler interno es el BFF falso; no se descarta con el cliente porque lo comparten ambos.
    private HttpClient Client(DelegatingHandler? handler)
    {
        HttpMessageHandler pipeline = Bff;
        if (handler is not null)
        {
            handler.InnerHandler = Bff;
            pipeline = handler;
        }

        return new HttpClient(pipeline, disposeHandler: false) { BaseAddress = new Uri(Options.BaseAddress!, "/") };
    }

    internal sealed record SignedIn(bool Ok);
}

/// <summary><see cref="ISecureStorage"/> en memoria; puede simular un valor ilegible.</summary>
internal sealed class FakeSecureStorage : ISecureStorage
{
    public Dictionary<string, string> Values { get; } = [];

    public bool FailReads { get; set; }

    public Task<string?> GetAsync(string key)
    {
        if (FailReads)
        {
            throw new InvalidOperationException("Unreadable secure value (test).");
        }

        return Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);
    }

    public Task SetAsync(string key, string value)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }

    public bool Remove(string key) => Values.Remove(key);

    public void RemoveAll() => Values.Clear();
}

/// <summary>Servicio de autenticación programable para los ViewModels.</summary>
internal sealed class FakeAuthService : IMobileAuthService
{
    public event EventHandler? SessionExpired;

    public Func<ClinicalSignInResult> LoginResult { get; set; } = () => new(ClinicalSignInStatus.Authenticated, Area: AccountArea.Paramedic);

    public Func<ClinicalSignInResult> VerifyResult { get; set; } = () => new(ClinicalSignInStatus.Authenticated, Area: AccountArea.Paramedic);

    public ClinicalSessionRestore RestoreResult { get; set; } = ClinicalSessionRestore.NoSession;

    /// <summary>Si no es null, el login espera a que el test lo libere.</summary>
    public TaskCompletionSource? LoginGate { get; set; }

    public List<(string Identifier, string Password)> Logins { get; } = [];

    public List<(string ChallengeToken, string? Code, string? RecoveryCode)> Verifications { get; } = [];

    public int LogoutCalls { get; private set; }

    public async Task<ClinicalSignInResult> LoginAsync(string identifier, string password, CancellationToken cancellationToken = default)
    {
        Logins.Add((identifier, password));
        if (LoginGate is { } gate)
        {
            await gate.Task;
        }

        return LoginResult();
    }

    public Task<ClinicalSignInResult> VerifyMfaCodeAsync(MfaChallengeDto challenge, string code, CancellationToken cancellationToken = default)
    {
        Verifications.Add((challenge.Token, code, null));
        return Task.FromResult(VerifyResult());
    }

    public Task<ClinicalSignInResult> VerifyRecoveryCodeAsync(MfaChallengeDto challenge, string recoveryCode, CancellationToken cancellationToken = default)
    {
        Verifications.Add((challenge.Token, null, recoveryCode));
        return Task.FromResult(VerifyResult());
    }

    public Task<ClinicalUser> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ClinicalSessionRestore> RestoreSessionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(RestoreResult);

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        LogoutCalls++;
        return Task.CompletedTask;
    }

    public void RaiseSessionExpired() => SessionExpired?.Invoke(this, EventArgs.Empty);
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
