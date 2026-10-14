using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication.Contracts;

namespace SCI_PH.Mobile.Common.Authentication;

public enum ClinicalSignInStatus
{
    /// <summary>Sesión iniciada y verificada con <c>/me</c>; puede entrar al área paramédica.</summary>
    Authenticated,

    /// <summary>La cuenta exige segundo factor: todavía no hay sesión.</summary>
    MfaRequired,

    /// <summary>Credenciales válidas pero sin <see cref="ClinicalPermissions.EmergenciesOperate"/>: la sesión se descarta.</summary>
    NoAccess
}

public sealed record ClinicalSignInResult(ClinicalSignInStatus Status, MfaChallengeDto? MfaChallenge = null);

public enum ClinicalSessionRestore
{
    /// <summary>No había refresh token, o el servidor lo rechazó (y se borró).</summary>
    NoSession,
    Restored,
    NoAccess,

    /// <summary>Sin conexión con el servidor: no hay sesión, pero el refresh token se conserva para otro intento.</summary>
    Unavailable
}

/// <summary>Renovación del access token para <see cref="AuthenticatedHttpHandler"/>.</summary>
public interface IClinicalSessionRefresher
{
    /// <summary>
    /// Renueva la sesión si <paramref name="staleAccessToken"/> sigue siendo el actual. Single-flight: con varias
    /// peticiones a la vez solo una llama a <c>/refresh</c> y el resto reutiliza su resultado.
    /// </summary>
    /// <returns><c>true</c> si hay un access token nuevo con el que reintentar.</returns>
    Task<bool> TryRefreshAsync(string staleAccessToken, CancellationToken cancellationToken);

    /// <summary>El servidor rechazó también el token renovado: se cierra la sesión.</summary>
    Task EndExpiredSessionAsync(CancellationToken cancellationToken);
}

/// <summary>Autenticación del personal clínico contra el Mobile BFF. Sin UI.</summary>
public interface IMobileAuthService
{
    /// <summary>La sesión terminó sin que el usuario la cerrara (refresh rechazado): la app vuelve a la entrada.</summary>
    event EventHandler? SessionExpired;

    Task<ClinicalSignInResult> LoginAsync(string identifier, string password, CancellationToken cancellationToken = default);

    Task<ClinicalSignInResult> VerifyMfaCodeAsync(MfaChallengeDto challenge, string code, CancellationToken cancellationToken = default);

    Task<ClinicalSignInResult> VerifyRecoveryCodeAsync(MfaChallengeDto challenge, string recoveryCode, CancellationToken cancellationToken = default);

    /// <summary>Relee <c>/me</c> con la sesión actual.</summary>
    Task<ClinicalUser> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>Al arrancar: renueva la sesión guardada y la valida con <c>/me</c> antes de mostrar el área clínica.</summary>
    Task<ClinicalSessionRestore> RestoreSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Revoca el refresh token en el servidor si se puede y SIEMPRE borra la sesión local.</summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Access token en memoria (<see cref="IClinicalSessionService"/>), refresh token en <see cref="ISecureTokenStore"/>.
/// Toda sesión nueva (login, MFA, restauración) pasa por <c>/me</c> y exige
/// <see cref="ClinicalPermissions.EmergenciesOperate"/>; sin él se revoca en el servidor y se borra.
/// Nunca registra contraseñas, códigos ni tokens.
/// </summary>
public sealed class MobileAuthService : IMobileAuthService, IClinicalSessionRefresher
{
    private readonly IMobileAuthApi _api;
    private readonly IClinicalSessionService _session;
    private readonly ISecureTokenStore _tokens;

    // Serializa refresh, restauración y logout: el refresh token rota y no puede usarse dos veces.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MobileAuthService(IMobileAuthApi api, IClinicalSessionService session, ISecureTokenStore tokens)
    {
        _api = api;
        _session = session;
        _tokens = tokens;
    }

    public event EventHandler? SessionExpired;

    public async Task<ClinicalSignInResult> LoginAsync(string identifier, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var response = await _api.LoginAsync(
            new LoginRequestDto { Identifier = identifier.Trim(), Password = password }, cancellationToken);

        if (response.MfaRequired)
        {
            return response.MfaChallenge is { Token.Length: > 0 } challenge
                ? new ClinicalSignInResult(ClinicalSignInStatus.MfaRequired, challenge)
                : throw new MobileApiException(MobileApiError.InvalidResponse);
        }

        return await CompleteSignInAsync(
            response.Session ?? throw new MobileApiException(MobileApiError.InvalidResponse), cancellationToken);
    }

    public Task<ClinicalSignInResult> VerifyMfaCodeAsync(MfaChallengeDto challenge, string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return VerifyMfaAsync(new MfaVerifyRequestDto { ChallengeToken = challenge.Token, Code = code.Trim() }, cancellationToken);
    }

    public Task<ClinicalSignInResult> VerifyRecoveryCodeAsync(MfaChallengeDto challenge, string recoveryCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryCode);
        return VerifyMfaAsync(
            new MfaVerifyRequestDto { ChallengeToken = challenge.Token, RecoveryCode = recoveryCode.Trim() }, cancellationToken);
    }

    public async Task<ClinicalUser> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = _session.AccessToken ?? throw new MobileApiException(MobileApiError.Unauthorized);
        var user = ToUser(await _api.GetCurrentUserAsync(accessToken, cancellationToken));
        _session.SetCurrentUser(user);
        return user;
    }

    public async Task<ClinicalSessionRestore> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var refreshToken = await _tokens.GetRefreshTokenAsync();
            if (refreshToken is null)
            {
                _session.Clear();
                return ClinicalSessionRestore.NoSession;
            }

            AuthenticationResultDto renewed;
            try
            {
                renewed = await _api.RefreshAsync(refreshToken, cancellationToken);
            }
            catch (MobileApiException ex) when (IsRejected(ex))
            {
                await ClearLocalAsync();
                return ClinicalSessionRestore.NoSession;
            }
            catch (MobileApiException)
            {
                _session.Clear();
                return ClinicalSessionRestore.Unavailable;
            }

            try
            {
                var result = await CompleteSignInAsync(renewed, cancellationToken);
                return result.Status == ClinicalSignInStatus.Authenticated
                    ? ClinicalSessionRestore.Restored
                    : ClinicalSessionRestore.NoAccess;
            }
            catch (MobileApiException)
            {
                // CompleteSignInAsync ya descartó la sesión.
                return ClinicalSessionRestore.Unavailable;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            await DiscardSessionAsync(await _tokens.GetRefreshTokenAsync());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryRefreshAsync(string staleAccessToken, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = _session.AccessToken;
            if (current is null)
            {
                // La sesión se cerró mientras esta petición esperaba.
                return false;
            }

            if (!string.Equals(current, staleAccessToken, StringComparison.Ordinal))
            {
                // Otra petición ya renovó la sesión: se reintenta con su token.
                return true;
            }

            var refreshToken = await _tokens.GetRefreshTokenAsync();
            if (refreshToken is null)
            {
                await ExpireAsync();
                return false;
            }

            AuthenticationResultDto renewed;
            try
            {
                renewed = await _api.RefreshAsync(refreshToken, cancellationToken);
            }
            catch (MobileApiException ex) when (IsRejected(ex))
            {
                // Refresh caducado, revocado o reutilizado (el servidor revoca toda la familia).
                await ExpireAsync();
                return false;
            }

            // Un fallo de red se propaga sin borrar nada: el refresh token sigue siendo válido.
            await _tokens.SaveRefreshTokenAsync(renewed.RefreshToken);
            _session.SetAuthentication(renewed.AccessToken, renewed.AccessTokenExpiresAtUtc);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EndExpiredSessionAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            await DiscardSessionAsync(await _tokens.GetRefreshTokenAsync());
        }
        finally
        {
            _gate.Release();
        }

        SessionExpired?.Invoke(this, EventArgs.Empty);
    }

    private async Task<ClinicalSignInResult> VerifyMfaAsync(MfaVerifyRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ChallengeToken);
        return await CompleteSignInAsync(await _api.VerifyMfaAsync(request, cancellationToken), cancellationToken);
    }

    // Tokens nuevos → refresh a SecureStorage, access a memoria → /me → permiso. Si algo falla, nada queda a medias.
    private async Task<ClinicalSignInResult> CompleteSignInAsync(AuthenticationResultDto result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.AccessToken) || string.IsNullOrWhiteSpace(result.RefreshToken))
        {
            throw new MobileApiException(MobileApiError.InvalidResponse);
        }

        await _tokens.SaveRefreshTokenAsync(result.RefreshToken);
        _session.SetAuthentication(result.AccessToken, result.AccessTokenExpiresAtUtc);

        ClinicalUser user;
        try
        {
            user = ToUser(await _api.GetCurrentUserAsync(result.AccessToken, cancellationToken));
        }
        catch
        {
            await DiscardSessionAsync(result.RefreshToken);
            throw;
        }

        if (!user.CanOperateEmergencies)
        {
            await DiscardSessionAsync(result.RefreshToken);
            return new ClinicalSignInResult(ClinicalSignInStatus.NoAccess);
        }

        _session.SetCurrentUser(user);
        return new ClinicalSignInResult(ClinicalSignInStatus.Authenticated);
    }

    // Revoca en el servidor si hay conexión; la limpieza local ocurre siempre.
    private async Task DiscardSessionAsync(string? refreshToken)
    {
        try
        {
            if (refreshToken is not null)
            {
                await _api.LogoutAsync(refreshToken, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // Sin conexión o servidor caído: el token se borra igualmente del dispositivo.
        }
        finally
        {
            await ClearLocalAsync();
        }
    }

    private async Task ExpireAsync()
    {
        await ClearLocalAsync();
        SessionExpired?.Invoke(this, EventArgs.Empty);
    }

    private async Task ClearLocalAsync()
    {
        _session.Clear();
        await _tokens.ClearAsync();
    }

    private static bool IsRejected(MobileApiException exception) =>
        exception.Error is MobileApiError.Unauthorized or MobileApiError.BadRequest;

    private static ClinicalUser ToUser(UserAccountDto account) =>
        new(account.Id, account.UserName, account.Email, account.Roles ?? [], account.Permissions ?? []);
}
