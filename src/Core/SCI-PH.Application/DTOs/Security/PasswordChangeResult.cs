using SCI_PH.Application.Common.Security;

namespace SCI_PH.Application.DTOs.Security
{
    // Errors contain only password-policy messages; they never describe the stored account.
    public sealed record PasswordChangeResult(bool Succeeded, SecurityFailureReason? FailureReason, IReadOnlyList<string> Errors)
    {
        public static PasswordChangeResult Success { get; } = new(true, null, []);

        public static PasswordChangeResult Failed(SecurityFailureReason reason, IReadOnlyList<string>? errors = null) =>
            new(false, reason, errors ?? []);
    }
}
