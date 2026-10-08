using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SCI_PH.Application.Common.Security;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Clinical.Seed
{
    // Idempotent: one demo clinic (by FacilityCode), one ClinicalStaff per license number and one open affiliation.
    // Administrators are never linked: an administrator does not become clinical staff implicitly.
    public sealed class ClinicalDevelopmentSeeder
    {
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

            await _clinical.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Clinical development seed applied to clinic {ClinicId}.", clinic.Id);
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
