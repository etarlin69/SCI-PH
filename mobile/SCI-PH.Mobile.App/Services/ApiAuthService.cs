using System.Net.Http.Headers;
using System.Net.Http.Json;
using SCI_PH.Mobile.App.Models.Security;

namespace SCI_PH.Mobile.App.Services;

public class ApiAuthService : IAuthService
{
    private readonly HttpClient _httpClient;

    public ApiAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LoginResultDto?> LoginAsync(string username, string password)
    {
        var request = new LoginRequestDto { Identifier = username, Password = password };
        var response = await _httpClient.PostAsJsonAsync("api/mobile/auth/login", request);

        if (!response.IsSuccessStatusCode)
            return null; // O manejar errores específicos (401 Unauthorized)

        var result = await response.Content.ReadFromJsonAsync<LoginResultDto>();
        
        if (result?.Session != null)
        {
            await SaveTokensAsync(result.Session);
        }

        return result;
    }

    public async Task<AuthenticationResultDto?> VerifyMfaAsync(string challengeToken, string code)
    {
        var request = new MfaVerifyRequestDto { ChallengeToken = challengeToken, Code = code };
        var response = await _httpClient.PostAsJsonAsync("api/mobile/auth/mfa/verify", request);

        if (!response.IsSuccessStatusCode)
            return null;

        var session = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        
        if (session != null)
        {
            await SaveTokensAsync(session);
        }

        return session;
    }

    public async Task<bool> RefreshTokenAsync()
    {
        var refreshToken = await SecureStorage.Default.GetAsync("refresh_token");
        if (string.IsNullOrEmpty(refreshToken)) return false;

        var request = new RefreshTokenRequestDto { RefreshToken = refreshToken };
        var response = await _httpClient.PostAsJsonAsync("api/mobile/auth/refresh", request);

        if (!response.IsSuccessStatusCode) return false;

        var session = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        if (session != null)
        {
            await SaveTokensAsync(session);
            return true;
        }

        return false;
    }

    public async Task LogoutAsync()
    {
        var refreshToken = await SecureStorage.Default.GetAsync("refresh_token");
        if (!string.IsNullOrEmpty(refreshToken))
        {
            var request = new RefreshTokenRequestDto { RefreshToken = refreshToken };
            await _httpClient.PostAsJsonAsync("api/mobile/auth/logout", request);
        }

        // Limpiar almacenamiento local sin importar si el servidor falló
        SecureStorage.Default.RemoveAll();
    }

    public async Task<UserAccountDto?> GetCurrentUserAsync()
    {
        await AddAuthorizationHeaderAsync();
        var response = await _httpClient.GetAsync("api/mobile/auth/me");
        
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<UserAccountDto>();
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var token = await SecureStorage.Default.GetAsync("access_token");
        return !string.IsNullOrEmpty(token);
    }

    // Métodos auxiliares para manejo de tokens
    private async Task SaveTokensAsync(AuthenticationResultDto session)
    {
        await SecureStorage.Default.SetAsync("access_token", session.AccessToken);
        await SecureStorage.Default.SetAsync("refresh_token", session.RefreshToken);
    }

    private async Task AddAuthorizationHeaderAsync()
    {
        var token = await SecureStorage.Default.GetAsync("access_token");
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}