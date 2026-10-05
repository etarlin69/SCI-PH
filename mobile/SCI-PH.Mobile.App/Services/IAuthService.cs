using SCI_PH.Mobile.App.Models.Security;

namespace SCI_PH.Mobile.App.Services;

public interface IAuthService
{
    Task<LoginResultDto?> LoginAsync(string username, string password);
    Task<AuthenticationResultDto?> VerifyMfaAsync(string challengeToken, string code);
    Task<bool> RefreshTokenAsync();
    Task LogoutAsync();
    Task<bool> IsAuthenticatedAsync();
    Task<UserAccountDto?> GetCurrentUserAsync();
}