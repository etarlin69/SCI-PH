using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Identity.Services
{
    // The only writer of Patient/ClinicalStaff.IdentityUserId. Uses the domain methods, so the clinical
    // rules (no silent re-linking) still apply. The unique index on IdentityUserId backs the race case.
    internal sealed class ClinicalIdentityLinkService : IClinicalIdentityLinkService
    {
        private readonly ClinicalDbContext _clinical;

        public ClinicalIdentityLinkService(ClinicalDbContext clinical) => _clinical = clinical;

        public async Task<ClinicalSubject?> FindSubjectAsync(InvitationSubjectType type, Guid subjectId, CancellationToken cancellationToken) =>
            type switch
            {
                InvitationSubjectType.Patient => await _clinical.Patients.AsNoTracking()
                    .Where(p => p.Id == subjectId)
                    .Select(p => new ClinicalSubject(
                        InvitationSubjectType.Patient,
                        p.Id,
                        p.IdentityUserId,
                        p.Identifiers
                            .Where(i => i.Type == PatientIdentifierType.Email && i.Status == PatientIdentifierStatus.Active)
                            .OrderByDescending(i => i.IsPrimary)
                            .ThenBy(i => i.CreatedAtUtc)
                            .Select(i => i.NormalizedValue)
                            .FirstOrDefault(),
                        true))
                    .SingleOrDefaultAsync(cancellationToken),
                InvitationSubjectType.ClinicalStaff => await _clinical.ClinicalStaffMembers.AsNoTracking()
                    .Where(s => s.Id == subjectId)
                    .Select(s => new ClinicalSubject(
                        InvitationSubjectType.ClinicalStaff, s.Id, s.IdentityUserId, null, s.Status == StaffStatus.Active))
                    .SingleOrDefaultAsync(cancellationToken),
                _ => null
            };

        public async Task<bool> LinkAsync(InvitationSubjectType type, Guid subjectId, Guid userId, CancellationToken cancellationToken)
        {
            var linked = type switch
            {
                InvitationSubjectType.Patient => await LinkPatientAsync(subjectId, userId, cancellationToken),
                InvitationSubjectType.ClinicalStaff => await LinkStaffAsync(subjectId, userId, cancellationToken),
                _ => false
            };

            if (!linked)
            {
                return false;
            }

            try
            {
                await _clinical.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException)
            {
                _clinical.ChangeTracker.Clear();
                return false;
            }
        }

        public Task<Guid?> FindPatientIdByUserAsync(Guid userId, CancellationToken cancellationToken) =>
            _clinical.Patients.AsNoTracking()
                .Where(p => p.IdentityUserId == userId)
                .Select(p => (Guid?)p.Id)
                .SingleOrDefaultAsync(cancellationToken);

        private async Task<bool> LinkPatientAsync(Guid patientId, Guid userId, CancellationToken cancellationToken)
        {
            var patient = await _clinical.Patients.SingleOrDefaultAsync(p => p.Id == patientId, cancellationToken);
            if (patient is not { IdentityUserId: null })
            {
                return false;
            }

            patient.LinkIdentityUser(userId);
            return true;
        }

        private async Task<bool> LinkStaffAsync(Guid staffId, Guid userId, CancellationToken cancellationToken)
        {
            var staff = await _clinical.ClinicalStaffMembers.SingleOrDefaultAsync(s => s.Id == staffId, cancellationToken);
            if (staff is not { IdentityUserId: null, Status: StaffStatus.Active })
            {
                return false;
            }

            staff.LinkIdentityUser(userId);
            return true;
        }
    }
}
