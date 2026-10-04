using System.ComponentModel.DataAnnotations;

namespace SCI_PH.Application.DTOs.Security
{
    // Short-lived proof that the password was correct; it only works on the MFA verification endpoint.
    public sealed record MfaChallenge(string Token, DateTimeOffset ExpiresAtUtc);

    // Validated content of an MFA challenge token.
    public sealed record MfaChallengeClaims(Guid UserId, string ChallengeId);

    // Exactly one of Session or Challenge is set on success.
    public sealed record LoginResult(AuthenticationResult? Session, MfaChallenge? Challenge)
    {
        public bool RequiresMfa => Challenge is not null;

        public static LoginResult WithSession(AuthenticationResult session) => new(session, null);

        public static LoginResult WithChallenge(MfaChallenge challenge) => new(null, challenge);
    }

    // Code is the authenticator (TOTP) code; RecoveryCode is a one-time backup code. Send one of them.
    public sealed record MfaVerifyRequest(
        [Required, MaxLength(2048)] string ChallengeToken,
        [MaxLength(16)] string? Code,
        [MaxLength(32)] string? RecoveryCode);
}
