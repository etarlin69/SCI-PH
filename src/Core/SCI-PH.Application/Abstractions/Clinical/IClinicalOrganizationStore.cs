using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Organizations;

namespace SCI_PH.Application.Abstractions.Clinical
{
    // Clinics, clinical staff and their affiliations for administration (clinical schema). Reads are no-tracking;
    // Find* return tracked aggregates and SaveChangesAsync writes everything added or changed in one transaction.
    public interface IClinicalOrganizationStore
    {
        // By name.
        Task<IReadOnlyList<ClinicAdminSummary>> ListClinicsAsync(string? search, ClinicStatus? status, CancellationToken cancellationToken);

        Task<ClinicAdminSummary?> GetClinicAsync(Guid clinicId, CancellationToken cancellationToken);

        Task<Clinic?> FindClinicAsync(Guid clinicId, CancellationToken cancellationToken);

        // By family and given names.
        Task<IReadOnlyList<ClinicalStaffAdminSummary>> ListStaffAsync(string? search, StaffStatus? status, CancellationToken cancellationToken);

        Task<ClinicalStaffAdminSummary?> GetStaffAsync(Guid staffId, CancellationToken cancellationToken);

        Task<ClinicalStaff?> FindStaffAsync(Guid staffId, CancellationToken cancellationToken);

        // Open first, then the most recent.
        Task<IReadOnlyList<ClinicStaffAffiliationSummary>> ListAffiliationsAsync(Guid staffId, CancellationToken cancellationToken);

        Task<bool> HasActiveAffiliationAsync(Guid staffId, Guid clinicId, CancellationToken cancellationToken);

        Task<ClinicStaffAffiliation?> FindAffiliationAsync(Guid staffId, Guid affiliationId, CancellationToken cancellationToken);

        void Add(Clinic clinic);

        void Add(ClinicalStaff staff);

        void Add(ClinicStaffAffiliation affiliation);

        void Add(ClinicalAuditEntry auditEntry);

        // Known uniqueness violations (facility code, license number, linked account, open affiliation) surface as
        // ResourceConflictException; anything else stays a server error.
        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
