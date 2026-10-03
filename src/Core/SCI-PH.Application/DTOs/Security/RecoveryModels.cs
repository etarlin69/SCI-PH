using System.ComponentModel.DataAnnotations;

namespace SCI_PH.Application.DTOs.Security
{
    public sealed record EmailRequest([Required, EmailAddress, MaxLength(256)] string Email);

    public sealed record ResetPasswordRequest(
        [Required] Guid UserId,
        [Required, MaxLength(2048)] string Token,
        [Required, MaxLength(256)] string NewPassword);

    public sealed record ConfirmEmailRequest(
        [Required] Guid UserId,
        [Required, MaxLength(2048)] string Token);

    // Account that may receive a recovery or confirmation email.
    public sealed record RecoveryTarget(Guid UserId, string Email, bool EmailConfirmed);

    public sealed record AccountCreation(Guid? UserId, bool EmailInUse, IReadOnlyList<string> Errors)
    {
        public bool Succeeded => UserId.HasValue;
    }
}
