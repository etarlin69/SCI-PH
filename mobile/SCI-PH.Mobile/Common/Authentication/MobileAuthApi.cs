using System.Net.Http.Headers;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication.Contracts;

namespace SCI_PH.Mobile.Common.Authentication;

/// <summary>Endpoints de <c>api/mobile/auth</c>, uno a uno, sin lógica de sesión.</summary>
public interface IMobileAuthApi
{
    Task<MobileLoginResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken);

    Task<AuthenticationResultDto> VerifyMfaAsync(MfaVerifyRequestDto request, CancellationToken cancellationToken);

    Task<AuthenticationResultDto> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken);

    Task<UserAccountDto> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken);
}

/// <summary>
/// Usa el cliente público (sin <see cref="AuthenticatedHttpHandler"/>): login, MFA, refresh y logout no llevan Bearer,
/// y el refresh nunca puede dispararse a sí mismo. <c>/me</c> lleva explícitamente el access token recién emitido.
/// </summary>
public sealed class MobileAuthApi : IMobileAuthApi
{
    private const string Base = "api/mobile/auth/";

    private readonly IMobileApiClient _client;

    public MobileAuthApi(IMobileApiClient publicClient)
    {
        _client = publicClient;
    }

    public Task<MobileLoginResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken) =>
        _client.PostAsync<LoginRequestDto, MobileLoginResponseDto>(Base + "login", request, cancellationToken);

    public Task<AuthenticationResultDto> VerifyMfaAsync(MfaVerifyRequestDto request, CancellationToken cancellationToken) =>
        _client.PostAsync<MfaVerifyRequestDto, AuthenticationResultDto>(Base + "mfa/verify", request, cancellationToken);

    public Task<AuthenticationResultDto> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        _client.PostAsync<RefreshTokenRequestDto, AuthenticationResultDto>(
            Base + "refresh", new RefreshTokenRequestDto { RefreshToken = refreshToken }, cancellationToken);

    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken) =>
        _client.PostAsync(Base + "logout", new RefreshTokenRequestDto { RefreshToken = refreshToken }, cancellationToken);

    public Task<UserAccountDto> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Base + "me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return _client.SendAsync<UserAccountDto>(request, cancellationToken);
    }
}
