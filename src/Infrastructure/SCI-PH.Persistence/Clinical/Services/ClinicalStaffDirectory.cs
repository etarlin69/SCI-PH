using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    // ClinicalStaff.IdentityUserId -> open ClinicStaffAffiliation -> active Clinic. The rules on top of this
    // (inactive staff, clinic selection) live in Application (CurrentClinicalActor).
    internal sealed class ClinicalStaffDirectory : IClinicalStaffDirectory
    {
        private readonly ClinicalDbContext _clinical;

        public ClinicalStaffDirectory(ClinicalDbContext clinical) => _clinical = clinical;

        public async Task<ClinicalStaffMembership?> FindByIdentityUserAsync(Guid identityUserId, CancellationToken cancellationToken)
        {
            var staff = await _clinical.ClinicalStaffMembers.AsNoTracking()
                .Where(s => s.IdentityUserId == identityUserId)
                .Select(s => new { s.Id, s.Status })
                .SingleOrDefaultAsync(cancellationToken);
            if (staff is null)
            {
                return null;
            }

            var clinicIds = await _clinical.ClinicStaffAffiliations.AsNoTracking()
                .Where(a => a.ClinicalStaffId == staff.Id && a.EndedAtUtc == null)
                .Join(_clinical.Clinics.Where(c => c.Status == ClinicStatus.Active), a => a.ClinicId, c => c.Id, (_, c) => c.Id)
                .Distinct()
                .ToListAsync(cancellationToken);

            return new ClinicalStaffMembership(staff.Id, staff.Status == StaffStatus.Active, clinicIds);
        }

        public async Task<IReadOnlyList<ClinicSummary>> GetClinicsAsync(IReadOnlyCollection<Guid> clinicIds, CancellationToken cancellationToken) =>
            await _clinical.Clinics.AsNoTracking()
                .Where(c => clinicIds.Contains(c.Id))
                .OrderBy(c => c.Name)
                .Select(c => new ClinicSummary(c.Id, c.Name))
                .ToListAsync(cancellationToken);
    }
}
