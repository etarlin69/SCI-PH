using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Identity.Services
{
    internal sealed class MfaChallengeStore : IMfaChallengeStore
    {
        private readonly IdentityDbContext _db;
        private readonly TimeProvider _time;

        public MfaChallengeStore(IdentityDbContext db, TimeProvider time)
        {
            _db = db;
            _time = time;
        }

        // Only the newest challenge of a user is valid; older and expired ones are deleted here, which also keeps
        // the table small without a background job.
        public async Task AddAsync(Guid userId, string challengeHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
        {
            var previous = await _db.MfaLoginChallenges.Where(c => c.UserId == userId).ToListAsync(cancellationToken);
            _db.MfaLoginChallenges.RemoveRange(previous);
            _db.MfaLoginChallenges.Add(MfaLoginChallenge.Issue(userId, challengeHash, _time.GetUtcNow(), expiresAtUtc));
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A concurrent login of the same user already removed a previous challenge; keep the new one.
                foreach (var entry in _db.ChangeTracker.Entries<MfaLoginChallenge>().Where(e => e.State == EntityState.Deleted).ToList())
                {
                    entry.State = EntityState.Detached;
                }

                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<Guid?> TryClaimAsync(Guid userId, string challengeHash, CancellationToken cancellationToken)
        {
            var now = _time.GetUtcNow();
            var challenge = await _db.MfaLoginChallenges.SingleOrDefaultAsync(
                c => c.ChallengeHash == challengeHash && c.UserId == userId && c.ConsumedAtUtc == null && c.ExpiresAtUtc > now,
                cancellationToken);
            if (challenge is null)
            {
                return null;
            }

            challenge.Consume(now);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return challenge.Id;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another request consumed it between the read and the conditional UPDATE.
                _db.Entry(challenge).State = EntityState.Detached;
                return null;
            }
        }

        public async Task ReleaseAsync(Guid challengeId, CancellationToken cancellationToken)
        {
            var challenge = await _db.MfaLoginChallenges.SingleOrDefaultAsync(
                c => c.Id == challengeId && c.ConsumedAtUtc != null, cancellationToken);
            if (challenge is null)
            {
                return;
            }

            challenge.Release();
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.Entry(challenge).State = EntityState.Detached;
            }
        }
    }
}
