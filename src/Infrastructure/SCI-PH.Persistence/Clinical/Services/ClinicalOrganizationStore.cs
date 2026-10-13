using Microsoft.EntityFrameworkCore;
using Npgsql;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    internal sealed class ClinicalOrganizationStore : IClinicalOrganizationStore
    {
        // Unique indexes whose violation is an expected conflict (concurrent or duplicate administration).
        private static readonly IReadOnlyDictionary<string, string> _knownUniqueViolations = new Dictionary<string, string>
        {
            ["IX_Clinics_FacilityCode"] = "A clinic with this facility code already exists.",
            ["IX_ClinicalStaffMembers_ProfessionalLicenseNumber"] = "A staff member with this professional license number already exists.",
            ["IX_ClinicalStaffMembers_IdentityUserId"] = "The account is already linked to another staff member.",
            ["UX_ClinicStaffAffiliations_Active"] = "The staff member already has an open affiliation with this clinic."
        };

        private readonly ClinicalDbContext _clinical;

        public ClinicalOrganizationStore(ClinicalDbContext clinical) => _clinical = clinical;

        public async Task<IReadOnlyList<ClinicAdminSummary>> ListClinicsAsync(string? search, ClinicStatus? status, CancellationToken cancellationToken)
        {
            var query = _clinical.Clinics.AsNoTracking();
            if (status is { } wanted)
            {
                query = query.Where(c => c.Status == wanted);
            }

            if (search is not null)
            {
                var term = search.ToLowerInvariant();
                query = query.Where(c => c.Name.ToLower().Contains(term)
                    || (c.FacilityCode != null && c.FacilityCode.ToLower().Contains(term))
                    || c.Province.ToLower().Contains(term)
                    || c.Municipality.ToLower().Contains(term));
            }

            return await ProjectClinics(query.OrderBy(c => c.Name)).ToListAsync(cancellationToken);
        }

        public Task<ClinicAdminSummary?> GetClinicAsync(Guid clinicId, CancellationToken cancellationToken) =>
            ProjectClinics(_clinical.Clinics.AsNoTracking().Where(c => c.Id == clinicId)).SingleOrDefaultAsync(cancellationToken);

        public Task<Clinic?> FindClinicAsync(Guid clinicId, CancellationToken cancellationToken) =>
            _clinical.Clinics.SingleOrDefaultAsync(c => c.Id == clinicId, cancellationToken);

        public async Task<IReadOnlyList<ClinicalStaffAdminSummary>> ListStaffAsync(string? search, StaffStatus? status, CancellationToken cancellationToken)
        {
            var query = _clinical.ClinicalStaffMembers.AsNoTracking();
            if (status is { } wanted)
            {
                query = query.Where(s => s.Status == wanted);
            }

            if (search is not null)
            {
                var term = search.ToLowerInvariant();
                query = query.Where(s => (s.GivenNames + " " + s.FamilyNames).ToLower().Contains(term)
                    || (s.ProfessionalLicenseNumber != null && s.ProfessionalLicenseNumber.ToLower().Contains(term)));
            }

            return await ProjectStaff(query.OrderBy(s => s.FamilyNames).ThenBy(s => s.GivenNames)).ToListAsync(cancellationToken);
        }

        public Task<ClinicalStaffAdminSummary?> GetStaffAsync(Guid staffId, CancellationToken cancellationToken) =>
            ProjectStaff(_clinical.ClinicalStaffMembers.AsNoTracking().Where(s => s.Id == staffId)).SingleOrDefaultAsync(cancellationToken);

        public Task<ClinicalStaff?> FindStaffAsync(Guid staffId, CancellationToken cancellationToken) =>
            _clinical.ClinicalStaffMembers.SingleOrDefaultAsync(s => s.Id == staffId, cancellationToken);

        public async Task<IReadOnlyList<ClinicStaffAffiliationSummary>> ListAffiliationsAsync(Guid staffId, CancellationToken cancellationToken) =>
            await _clinical.ClinicStaffAffiliations.AsNoTracking()
                .Where(a => a.ClinicalStaffId == staffId)
                .Join(_clinical.Clinics, a => a.ClinicId, c => c.Id, (a, c) => new { Affiliation = a, Clinic = c })
                .OrderBy(x => x.Affiliation.EndedAtUtc != null).ThenByDescending(x => x.Affiliation.StartedAtUtc)
                .Select(x => new ClinicStaffAffiliationSummary(
                    x.Affiliation.Id, x.Clinic.Id, x.Clinic.Name, x.Clinic.Status, x.Affiliation.StartedAtUtc, x.Affiliation.EndedAtUtc,
                    x.Affiliation.EndedAtUtc == null))
                .ToListAsync(cancellationToken);

        public Task<bool> HasActiveAffiliationAsync(Guid staffId, Guid clinicId, CancellationToken cancellationToken) =>
            _clinical.ClinicStaffAffiliations.AnyAsync(
                a => a.ClinicalStaffId == staffId && a.ClinicId == clinicId && a.EndedAtUtc == null, cancellationToken);

        public Task<ClinicStaffAffiliation?> FindAffiliationAsync(Guid staffId, Guid affiliationId, CancellationToken cancellationToken) =>
            _clinical.ClinicStaffAffiliations.SingleOrDefaultAsync(a => a.Id == affiliationId && a.ClinicalStaffId == staffId, cancellationToken);

        public void Add(Clinic clinic) => _clinical.Clinics.Add(clinic);

        public void Add(ClinicalStaff staff) => _clinical.ClinicalStaffMembers.Add(staff);

        public void Add(ClinicStaffAffiliation affiliation) => _clinical.ClinicStaffAffiliations.Add(affiliation);

        public void Add(ClinicalAuditEntry auditEntry) => _clinical.ClinicalAuditEntries.Add(auditEntry);

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _clinical.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: { } constraint
            } && _knownUniqueViolations.TryGetValue(constraint, out var message))
            {
                _clinical.ChangeTracker.Clear();
                throw new ResourceConflictException(message, exception);
            }
        }

        private IQueryable<ClinicAdminSummary> ProjectClinics(IQueryable<Clinic> clinics) =>
            clinics.Select(c => new ClinicAdminSummary(
                c.Id, c.Name, c.Type, c.FacilityCode, c.Province, c.Municipality, c.Address, c.PhoneNumber, c.Status,
                _clinical.ClinicStaffAffiliations.Count(a => a.ClinicId == c.Id && a.EndedAtUtc == null)));

        private IQueryable<ClinicalStaffAdminSummary> ProjectStaff(IQueryable<ClinicalStaff> staff) =>
            staff.Select(s => new ClinicalStaffAdminSummary(
                s.Id, s.GivenNames, s.FamilyNames, s.Type, s.Status, s.ProfessionalLicenseNumber, s.Specialty, s.PhoneNumber,
                s.IdentityUserId != null, s.IdentityUserId,
                _clinical.ClinicStaffAffiliations
                    .Where(a => a.ClinicalStaffId == s.Id && a.EndedAtUtc == null)
                    .Join(_clinical.Clinics, a => a.ClinicId, c => c.Id, (_, c) => c)
                    .OrderBy(c => c.Name)
                    .Select(c => new ClinicSummary(c.Id, c.Name))
                    .ToList()));
    }
}
