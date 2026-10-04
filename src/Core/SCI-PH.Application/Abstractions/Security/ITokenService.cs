using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Abstractions.Security
{
    public interface ITokenService
    {
        AccessToken CreateAccessToken(UserAccount account);

        GeneratedRefreshToken CreateRefreshToken();

        string HashRefreshToken(string refreshToken);

        // Short-lived token proving the password step of an MFA login. It cannot be used as an access token.
        // challengeId (random, high entropy) becomes the jti; its hash is persisted to make the challenge single-use.
        MfaChallenge CreateMfaChallenge(Guid userId, string challengeId);

        // Returns user and challenge id, or null when the token is invalid, expired or not an MFA challenge.
        Task<MfaChallengeClaims?> ReadMfaChallengeAsync(string challengeToken);
    }
}
