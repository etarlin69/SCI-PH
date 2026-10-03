using System.ComponentModel.DataAnnotations;

namespace SCI_PH.Application.DTOs.Security
{
    public sealed record UserSummary(
        Guid Id,
        string UserName,
        string? Email,
        bool EmailConfirmed,
        bool IsActive,
        bool TwoFactorEnabled,
        DateTimeOffset? LockedOutUntilUtc,
        IReadOnlyList<string> Roles,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? LastLoginAtUtc);

    public sealed record UserPage(IReadOnlyList<UserSummary> Items, int Page, int PageSize, int Total);

    public sealed record SetRolesRequest([Required, MaxLength(8)] IReadOnlyList<string> Roles);

    public enum UserAdministrationOutcome
    {
        Succeeded = 1,
        NotFound = 2,
        InvalidRoles = 3,
        LastAdministrator = 4,
        SelfModification = 5
    }
}
