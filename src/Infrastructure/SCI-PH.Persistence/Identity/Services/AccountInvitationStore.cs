using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Identity.Services
{
    internal sealed class AccountInvitationStore : IAccountInvitationStore
    {
        private readonly IdentityDbContext _db;
        private readonly TimeProvider _time;

        public AccountInvitationStore(IdentityDbContext db, TimeProvider time)
        {
            _db = db;
            _time = time;
        }

        // Two statements in one transaction: the previous pending invitation must be revoked before the new
        // row is inserted, otherwise the filtered unique index (one pending per subject) would reject it.
        public async Task<Guid> AddAsync(NewAccountInvitation invitation, CancellationToken cancellationToken)
        {
            var now = _time.GetUtcNow();
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var pending = await _db.AccountInvitations
                .Where(i => i.SubjectType == invitation.SubjectType
                    && i.SubjectId == invitation.SubjectId
                    && i.Status == AccountInvitationStatus.Pending)
                .ToListAsync(cancellationToken);
            pending.ForEach(previous => previous.Revoke(now));
            await _db.SaveChangesAsync(cancellationToken);

            var created = AccountInvitation.Issue(invitation, now);
            _db.AccountInvitations.Add(created);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return created.Id;
        }

        public async Task<StoredAccountInvitation?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            (await _db.AccountInvitations.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken))?.ToStored();

        public async Task<StoredAccountInvitation?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            (await _db.AccountInvitations.AsNoTracking().SingleOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken))?.ToStored();

        // Status is a concurrency token: two simultaneous activations cannot both claim the invitation.
        public async Task<bool> TryClaimAsync(Guid id, CancellationToken cancellationToken)
        {
            var invitation = await _db.AccountInvitations.SingleOrDefaultAsync(
                i => i.Id == id && i.Status == AccountInvitationStatus.Pending, cancellationToken);
            if (invitation is null)
            {
                return false;
            }

            invitation.Claim(_time.GetUtcNow());
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.Entry(invitation).State = EntityState.Detached;
                return false;
            }
        }

        public async Task CompleteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
        {
            var invitation = await _db.AccountInvitations.SingleAsync(i => i.Id == id, cancellationToken);
            invitation.Complete(userId);
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task ReleaseAsync(Guid id, CancellationToken cancellationToken)
        {
            var invitation = await _db.AccountInvitations.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
            if (invitation is { IsClaimedWithoutAccount: true })
            {
                invitation.Release();
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        // Pending invitations, and claimed ones that never produced an account, can be revoked.
        public async Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken)
        {
            var invitation = await _db.AccountInvitations.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
            if (invitation is null || !(invitation.Status == AccountInvitationStatus.Pending || invitation.IsClaimedWithoutAccount))
            {
                return false;
            }

            invitation.Revoke(_time.GetUtcNow());
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
