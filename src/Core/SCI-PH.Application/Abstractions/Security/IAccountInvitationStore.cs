using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Abstractions.Security
{
    public interface IAccountInvitationStore
    {
        // Stores the invitation and revokes any other pending invitation of the same subject.
        Task<Guid> AddAsync(NewAccountInvitation invitation, CancellationToken cancellationToken);

        Task<StoredAccountInvitation?> FindAsync(Guid id, CancellationToken cancellationToken);

        Task<StoredAccountInvitation?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

        // Atomically moves a pending invitation to Accepted. Returns false if it was no longer pending.
        Task<bool> TryClaimAsync(Guid id, CancellationToken cancellationToken);

        // Records the account created from a claimed invitation.
        Task CompleteAsync(Guid id, Guid userId, CancellationToken cancellationToken);

        // Returns a claimed invitation to Pending when activation could not be completed.
        Task ReleaseAsync(Guid id, CancellationToken cancellationToken);

        Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken);
    }
}
