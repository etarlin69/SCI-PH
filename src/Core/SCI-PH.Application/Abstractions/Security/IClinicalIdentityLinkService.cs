using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Abstractions.Security
{
    // Bridge between accounts and clinical records through Patient/ClinicalStaff.IdentityUserId.
    // There is no foreign key between schemas; this service is the only place that writes the link.
    public interface IClinicalIdentityLinkService
    {
        Task<ClinicalSubject?> FindSubjectAsync(InvitationSubjectType type, Guid subjectId, CancellationToken cancellationToken);

        // Links only when the record exists and has no account yet. Returns false otherwise.
        Task<bool> LinkAsync(InvitationSubjectType type, Guid subjectId, Guid userId, CancellationToken cancellationToken);

        Task<Guid?> FindPatientIdByUserAsync(Guid userId, CancellationToken cancellationToken);
    }
}
