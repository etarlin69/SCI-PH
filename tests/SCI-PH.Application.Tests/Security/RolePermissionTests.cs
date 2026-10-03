using SCI_PH.Application.Common.Security;

namespace SCI_PH.Application.Tests.Security
{
    public class RolePermissionTests
    {
        [Fact]
        public void RolesAreTheExpectedUniqueSet()
        {
            Assert.Equal(
                ["Administrator", "Doctor", "Nurse", "Paramedic", "Receptionist", "Patient"],
                SecurityRoles.All);
            Assert.Equal(SecurityRoles.All.Count, SecurityRoles.All.Distinct().Count());
        }

        [Fact]
        public void PermissionsAreUniqueAndFollowTheAreaActionFormat()
        {
            Assert.NotEmpty(Permissions.All);
            Assert.Equal(Permissions.All.Count, Permissions.All.Distinct().Count());
            Assert.All(Permissions.All, permission => Assert.Matches("^[A-Z][A-Za-z]+\\.[A-Z][A-Za-z]+$", permission));
        }

        [Fact]
        public void MatrixCoversEveryRoleWithKnownPermissionsOnly()
        {
            Assert.Equal(SecurityRoles.All.Order(), RolePermissions.ByRole.Keys.Order());
            foreach (var (role, permissions) in RolePermissions.ByRole)
            {
                Assert.All(permissions, permission => Assert.Contains(permission, Permissions.All));
                Assert.True(permissions.Count == permissions.Distinct().Count(), $"{role} has duplicated permissions.");
            }
        }

        [Fact]
        public void AdministratorHasEveryPermission()
        {
            Assert.Equal(Permissions.All.Order(), RolePermissions.For(SecurityRoles.Administrator).Order());
        }

        [Fact]
        public void PatientRoleNeverGrantsGenericRecordAccess()
        {
            Assert.Empty(RolePermissions.For(SecurityRoles.Patient));
        }

        [Fact]
        public void OnlyAdministratorsManageUsersAndReadTheAudit()
        {
            foreach (var role in SecurityRoles.All.Where(role => role != SecurityRoles.Administrator))
            {
                Assert.DoesNotContain(Permissions.Users.Manage, RolePermissions.For(role));
                Assert.DoesNotContain(Permissions.Audit.Read, RolePermissions.For(role));
            }
        }

        [Fact]
        public void OnlyAdministratorsManageDevicesAndReceptionCanInvitePatients()
        {
            Assert.Equal([SecurityRoles.Administrator],
                SecurityRoles.All.Where(role => RolePermissions.For(role).Contains(Permissions.Devices.Manage)));
            Assert.Equal([SecurityRoles.Administrator, SecurityRoles.Receptionist],
                SecurityRoles.All.Where(role => RolePermissions.For(role).Contains(Permissions.Accounts.InvitePatient)));
            Assert.DoesNotContain(SecurityRoles.Administrator, SecurityRoles.InvitableStaff);
            Assert.DoesNotContain(SecurityRoles.Patient, SecurityRoles.InvitableStaff);
        }

        [Fact]
        public void ClinicalRolesMatchTheirResponsibilities()
        {
            Assert.Contains(Permissions.Prescriptions.Write, RolePermissions.For(SecurityRoles.Doctor));
            Assert.Contains(Permissions.Diagnoses.Write, RolePermissions.For(SecurityRoles.Doctor));
            Assert.Contains(Permissions.MedicationAdministrations.Write, RolePermissions.For(SecurityRoles.Nurse));
            Assert.DoesNotContain(Permissions.Prescriptions.Write, RolePermissions.For(SecurityRoles.Nurse));
            Assert.Contains(Permissions.Patients.Identify, RolePermissions.For(SecurityRoles.Paramedic));
            Assert.Contains(Permissions.Transfers.Manage, RolePermissions.For(SecurityRoles.Paramedic));
            Assert.Contains(Permissions.Encounters.Create, RolePermissions.For(SecurityRoles.Paramedic));
        }

        [Fact]
        public void ReceptionistHasNoClinicalWriteOrReadAccess()
        {
            var receptionist = RolePermissions.For(SecurityRoles.Receptionist);

            Assert.DoesNotContain(Permissions.ClinicalNotes.Write, receptionist);
            Assert.DoesNotContain(Permissions.Diagnoses.Write, receptionist);
            Assert.DoesNotContain(Permissions.Prescriptions.Write, receptionist);
            Assert.DoesNotContain(Permissions.Encounters.Read, receptionist);
            Assert.DoesNotContain(Permissions.Documents.Read, receptionist);
        }
    }
}
