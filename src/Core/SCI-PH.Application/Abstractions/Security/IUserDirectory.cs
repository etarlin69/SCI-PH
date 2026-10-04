using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Abstractions.Security
{
    // Account administration over ASP.NET Core Identity.
    public interface IUserDirectory
    {
        Task<UserPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

        Task<UserSummary?> GetAsync(Guid userId, CancellationToken cancellationToken);

        // Replaces the roles and renews the security stamp. Returns false when the account does not exist.
        Task<bool> SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);

        Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken);

        Task<bool> IsEmailInUseAsync(string email, CancellationToken cancellationToken);

        // Creates an inactive account with a confirmed email and the given role (invitation activation).
        Task<AccountCreation> CreateInvitedAccountAsync(string email, string password, string role, CancellationToken cancellationToken);

        // Compensation for an activation that could not be completed; only for accounts that never logged in.
        Task DeleteUnusedAccountAsync(Guid userId, CancellationToken cancellationToken);
    }
}
