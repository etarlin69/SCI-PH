using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Persistence.Identity.Models
{
    // One-time invitation to create an account for an existing Patient or ClinicalStaff record.
    // SubjectId is a plain Guid (no cross-schema foreign key). Only the token hash is stored.
    public sealed class AccountInvitation
    {
        private AccountInvitation() { }

        public Guid Id { get; private set; }
        public InvitationSubjectType SubjectType { get; private set; }
        public Guid SubjectId { get; private set; }
        public string Email { get; private set; } = string.Empty;
        public string IntendedRole { get; private set; } = string.Empty;
        public string TokenHash { get; private set; } = string.Empty;
        public AccountInvitationStatus Status { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset ExpiresAtUtc { get; private set; }
        public DateTimeOffset? AcceptedAtUtc { get; private set; }
        public Guid? AcceptedByUserId { get; private set; }
        public DateTimeOffset? RevokedAtUtc { get; private set; }
        public Guid? CreatedByUserId { get; private set; }

        // Claimed by an activation in progress that has not produced an account yet.
        public bool IsClaimedWithoutAccount => Status == AccountInvitationStatus.Accepted && AcceptedByUserId is null;

        public static AccountInvitation Issue(NewAccountInvitation invitation, DateTimeOffset createdAtUtc) =>
            new()
            {
                Id = Guid.NewGuid(),
                SubjectType = invitation.SubjectType,
                SubjectId = invitation.SubjectId,
                Email = invitation.Email.Trim(),
                IntendedRole = invitation.IntendedRole,
                TokenHash = invitation.TokenHash,
                Status = AccountInvitationStatus.Pending,
                CreatedAtUtc = createdAtUtc.ToUniversalTime(),
                ExpiresAtUtc = invitation.ExpiresAtUtc.ToUniversalTime(),
                CreatedByUserId = invitation.CreatedByUserId
            };

        public void Claim(DateTimeOffset claimedAtUtc)
        {
            Status = AccountInvitationStatus.Accepted;
            AcceptedAtUtc = claimedAtUtc.ToUniversalTime();
        }

        public void Complete(Guid userId) => AcceptedByUserId = userId;

        public void Release()
        {
            Status = AccountInvitationStatus.Pending;
            AcceptedAtUtc = null;
        }

        public void Revoke(DateTimeOffset revokedAtUtc)
        {
            Status = AccountInvitationStatus.Revoked;
            AcceptedAtUtc = null;
            RevokedAtUtc = revokedAtUtc.ToUniversalTime();
        }

        public StoredAccountInvitation ToStored() =>
            new(Id, SubjectType, SubjectId, Email, IntendedRole, Status, ExpiresAtUtc);
    }
}
