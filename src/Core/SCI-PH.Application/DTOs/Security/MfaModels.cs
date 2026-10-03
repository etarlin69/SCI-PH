using System.ComponentModel.DataAnnotations;

namespace SCI_PH.Application.DTOs.Security
{
    // SharedKey and AuthenticatorUri are shown once to configure the authenticator app; never logged.
    public sealed record MfaSetup(string SharedKey, string AuthenticatorUri);

    public sealed record MfaCodeRequest([Required, MaxLength(16)] string Code);

    // Recovery codes are shown only when MFA is enabled or the codes are regenerated.
    public sealed record RecoveryCodes(IReadOnlyList<string> Codes);

    public enum MfaCodeCheck
    {
        Succeeded = 1,
        Invalid = 2,
        LockedOut = 3
    }
}
