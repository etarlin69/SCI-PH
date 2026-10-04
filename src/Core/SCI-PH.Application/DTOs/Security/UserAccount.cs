namespace SCI_PH.Application.DTOs.Security
{
    // Account view used for tokens and /me. Never contains password data, stamps or clinical information.
    public sealed record UserAccount(
        Guid Id,
        string UserName,
        string? Email,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Permissions);
}
