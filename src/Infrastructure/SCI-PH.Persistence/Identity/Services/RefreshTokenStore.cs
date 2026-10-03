using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Identity.Services
{
    internal sealed class RefreshTokenStore : IRefreshTokenStore
    {
        private const int MaxRevocationAttempts = 3;

        private readonly IdentityDbContext _db;
        private readonly TimeProvider _time;

        public RefreshTokenStore(IdentityDbContext db, TimeProvider time)
        {
            _db = db;
            _time = time;
        }

        public async Task AddAsync(NewRefreshToken token, CancellationToken cancellationToken)
        {
            _db.RefreshTokens.Add(RefreshToken.Issue(token, _time.GetUtcNow()));
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task<StoredRefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken)
        {
            var token = await _db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
            return token?.ToStored();
        }

        // RevokedAtUtc is a concurrency token, so only one request can rotate a given token.
        public async Task<bool> RotateAsync(
            Guid currentTokenId,
            NewRefreshToken replacement,
            string? ipAddress,
            CancellationToken cancellationToken)
        {
            var current = await _db.RefreshTokens.SingleOrDefaultAsync(
                t => t.Id == currentTokenId && t.RevokedAtUtc == null, cancellationToken);
            if (current is null)
            {
                return false;
            }

            var now = _time.GetUtcNow();
            var next = RefreshToken.Issue(replacement, now);
            current.Revoke(now, RefreshTokenRevocationReason.Rotated, ipAddress, next.Id);
            _db.RefreshTokens.Add(next);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                DetachRefreshTokens();
                return false;
            }
        }

        public Task<int> RevokeFamilyAsync(
            Guid familyId,
            RefreshTokenRevocationReason reason,
            string? ipAddress,
            CancellationToken cancellationToken) =>
            RevokeActiveAsync(t => t.FamilyId == familyId, reason, ipAddress, cancellationToken);

        public Task<int> RevokeAllForUserAsync(
            Guid userId,
            RefreshTokenRevocationReason reason,
            string? ipAddress,
            CancellationToken cancellationToken) =>
            RevokeActiveAsync(t => t.UserId == userId, reason, ipAddress, cancellationToken);

        // Retries when a concurrent request revoked one of the tokens first.
        private async Task<int> RevokeActiveAsync(
            Expression<Func<RefreshToken, bool>> scope,
            RefreshTokenRevocationReason reason,
            string? ipAddress,
            CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                var tokens = await _db.RefreshTokens.Where(scope).Where(t => t.RevokedAtUtc == null).ToListAsync(cancellationToken);
                var now = _time.GetUtcNow();
                tokens.ForEach(token => token.Revoke(now, reason, ipAddress));

                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return tokens.Count;
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxRevocationAttempts)
                {
                    DetachRefreshTokens();
                }
            }
        }

        private void DetachRefreshTokens()
        {
            foreach (var entry in _db.ChangeTracker.Entries<RefreshToken>().ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
