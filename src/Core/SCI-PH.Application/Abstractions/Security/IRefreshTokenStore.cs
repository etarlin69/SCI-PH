using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Abstractions.Security
{
    // Persists refresh-token hashes. Raw token values never reach this abstraction.
    public interface IRefreshTokenStore
    {
        Task AddAsync(NewRefreshToken token, CancellationToken cancellationToken);

        Task<StoredRefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

        // Atomically revokes the current token (read as active by the caller) and stores its replacement. When the token
        // is no longer active at update time, tells a concurrent rotation of the same token (LostConcurrencyRace) apart
        // from any other revocation (Revoked); in both cases nothing is written.
        Task<RefreshTokenRotationResult> RotateAsync(Guid currentTokenId, NewRefreshToken replacement, string? ipAddress, CancellationToken cancellationToken);

        Task<int> RevokeFamilyAsync(Guid familyId, RefreshTokenRevocationReason reason, string? ipAddress, CancellationToken cancellationToken);

        Task<int> RevokeAllForUserAsync(Guid userId, RefreshTokenRevocationReason reason, string? ipAddress, CancellationToken cancellationToken);
    }
}
