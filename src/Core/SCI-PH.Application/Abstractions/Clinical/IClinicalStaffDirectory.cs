using SCI_PH.Application.DTOs.Clinical;

namespace SCI_PH.Application.Abstractions.Clinical
{
    // Read-only view of ClinicalStaff and their clinic affiliations, resolved from the Identity user id.
    public interface IClinicalStaffDirectory
    {
        Task<ClinicalStaffMembership?> FindByIdentityUserAsync(Guid identityUserId, CancellationToken cancellationToken);

        Task<IReadOnlyList<ClinicSummary>> GetClinicsAsync(IReadOnlyCollection<Guid> clinicIds, CancellationToken cancellationToken);
    }

    // ActiveClinicIds: clinics with an open affiliation (EndedAtUtc null) whose clinic is active.
    public sealed record ClinicalStaffMembership(Guid StaffId, bool IsActive, IReadOnlyCollection<Guid> ActiveClinicIds);
}
