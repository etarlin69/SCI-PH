using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Abstractions.Security
{
    // Persists refresh-token hashes. Raw token values never reach this abstraction.
    public interface IRefreshTokenStore
    {
        Task AddAsync(NewRefreshToken token, CancellationToken cancellationToken);

        Task<StoredRefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

        // Atomically revokes the current token and stores its replacement.
        // Returns false when the current token was already revoked (for example, a concurrent refresh).
        Task<bool> RotateAsync(Guid currentTokenId, NewRefreshToken replacement, string? ipAddress, CancellationToken cancellationToken);

        Task<int> RevokeFamilyAsync(Guid familyId, RefreshTokenRevocationReason reason, string? ipAddress, CancellationToken cancellationToken);

        Task<int> RevokeAllForUserAsync(Guid userId, RefreshTokenRevocationReason reason, string? ipAddress, CancellationToken cancellationToken);
    }
}
