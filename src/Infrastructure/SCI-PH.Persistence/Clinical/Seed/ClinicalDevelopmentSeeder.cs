using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.Common.Security;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Clinical.Seed
{
    // Idempotent: one demo clinic (by FacilityCode), one ClinicalStaff per license number and one open affiliation, plus
    // a small demo medication catalog (by Code). Administrators are never linked: an administrator does not become
    // clinical staff implicitly.
    public sealed class ClinicalDevelopmentSeeder
    {
        // Demo catalog for the hospital web in Development. The "DEV-" codes keep it apart from any real catalog.
        private static readonly (string Code, string Name, string Presentation, string Concentration, MedicationRoute Route)[] _demoMedications =
        [
            ("DEV-PARACETAMOL-500", "Paracetamol", "Tableta", "500 mg", MedicationRoute.Oral),
            ("DEV-IBUPROFENO-400", "Ibuprofeno", "Tableta", "400 mg", MedicationRoute.Oral),
            ("DEV-METAMIZOL-1G", "Metamizol", "Solución inyectable", "1 g/2 mL", MedicationRoute.Intravenous),
            ("DEV-SALINA-09", "Cloruro de sodio 0,9 %", "Solución para infusión", "1000 mL", MedicationRoute.Intravenous),
            ("DEV-OMEPRAZOL-40", "Omeprazol", "Polvo para solución inyectable", "40 mg", MedicationRoute.Intravenous),
            ("DEV-SALBUTAMOL-100", "Salbutamol", "Aerosol para inhalación", "100 mcg/dosis", MedicationRoute.Inhalation)
        ];

        private readonly ClinicalDbContext _clinical;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ClinicalDevelopmentSeedOptions _options;
        private readonly TimeProvider _time;
        private readonly ILogger<ClinicalDevelopmentSeeder> _logger;

        public ClinicalDevelopmentSeeder(
            ClinicalDbContext clinical,
            UserManager<ApplicationUser> users,
            IOptions<ClinicalDevelopmentSeedOptions> options,
            TimeProvider time,
            ILogger<ClinicalDevelopmentSeeder> logger)
        {
            _clinical = clinical;
            _users = users;
            _options = options.Value;
            _time = time;
            _logger = logger;
        }

        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            var facilityCode = _options.FacilityCode.Trim().ToUpperInvariant();
            var clinic = await _clinical.Clinics.SingleOrDefaultAsync(c => c.FacilityCode == facilityCode, cancellationToken);
            if (clinic is null)
            {
                clinic = Clinic.Register(_options.ClinicName, ClinicType.Hospital, "Distrito Nacional", "Santo Domingo", facilityCode);
                _clinical.Clinics.Add(clinic);
            }

            foreach (var entry in _options.Staff.Where(s => !string.IsNullOrWhiteSpace(s.LicenseNumber)))
            {
                var license = entry.LicenseNumber.Trim().ToUpperInvariant();
                var staff = await _clinical.ClinicalStaffMembers.SingleOrDefaultAsync(s => s.ProfessionalLicenseNumber == license, cancellationToken);
                if (staff is null)
                {
                    staff = ClinicalStaff.Register(entry.GivenNames, entry.FamilyNames, entry.Type, license);
                    _clinical.ClinicalStaffMembers.Add(staff);
                }

                var affiliated = await _clinical.ClinicStaffAffiliations.AnyAsync(
                    a => a.ClinicId == clinic.Id && a.ClinicalStaffId == staff.Id && a.EndedAtUtc == null, cancellationToken);
                if (!affiliated)
                {
                    _clinical.ClinicStaffAffiliations.Add(ClinicStaffAffiliation.Start(clinic.Id, staff.Id, _time.GetUtcNow()));
                }

                await LinkExistingAccountAsync(staff, entry.Email, cancellationToken);
                // Ids are not secrets: the administrator needs them to send the staff invitation.
                _logger.LogInformation("Clinical development seed: staff {LicenseNumber} is {StaffId}.", license, staff.Id);
            }

            await SeedMedicationsAsync(cancellationToken);
            await SeedFingerprintReferencesAsync(cancellationToken);
            await _clinical.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Clinical development seed applied to clinic {ClinicId}.", clinic.Id);
        }

        private async Task SeedMedicationsAsync(CancellationToken cancellationToken)
        {
            var codes = _demoMedications.Select(m => m.Code).ToList();
            var existing = await _clinical.Medications
                .Where(m => m.Code != null && codes.Contains(m.Code))
                .Select(m => m.Code!)
                .ToListAsync(cancellationToken);
            foreach (var (code, name, presentation, concentration, route) in _demoMedications.Where(m => !existing.Contains(m.Code)))
            {
                _clinical.Medications.Add(Medication.Register(name, presentation, concentration, code, route));
            }
        }

        // A reference to an unknown patient is skipped. The subject reference is never logged.
        private async Task SeedFingerprintReferencesAsync(CancellationToken cancellationToken)
        {
            foreach (var entry in _options.FingerprintReferences)
            {
                if (entry.Slot < 0 || entry.DeviceCredentialId == Guid.Empty
                    || !await _clinical.Patients.AnyAsync(p => p.Id == entry.PatientId, cancellationToken))
                {
                    _logger.LogWarning("Clinical development seed: a fingerprint reference was skipped (unknown patient or invalid reader/slot).");
                    continue;
                }

                var subject = FingerprintReferences.SubjectReference(entry.DeviceCredentialId, entry.Slot);
                if (!await _clinical.PatientBiometricReferences.AnyAsync(
                    r => r.Provider == FingerprintReferences.Provider && r.Modality == BiometricModality.Fingerprint && r.SubjectReference == subject,
                    cancellationToken))
                {
                    _clinical.PatientBiometricReferences.Add(PatientBiometricReference.Register(
                        entry.PatientId, BiometricModality.Fingerprint, FingerprintReferences.Provider, subject));
                }
            }
        }

        private async Task LinkExistingAccountAsync(ClinicalStaff staff, string? email, CancellationToken cancellationToken)
        {
            if (staff.IdentityUserId is not null || string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            var user = await _users.FindByEmailAsync(email.Trim());
            if (user is null || await _users.IsInRoleAsync(user, SecurityRoles.Administrator))
            {
                _logger.LogWarning("Clinical development seed: staff {StaffId} was not linked (no such account, or an administrator).", staff.Id);
                return;
            }

            if (!await _clinical.ClinicalStaffMembers.AnyAsync(s => s.IdentityUserId == user.Id, cancellationToken))
            {
                staff.LinkIdentityUser(user.Id);
            }
        }
    }
}
