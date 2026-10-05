namespace SCI_PH.Mobile.App.Models.Security;

public record UserAccountDto(
    Guid Id,
    string UserName,
    string? Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions
);