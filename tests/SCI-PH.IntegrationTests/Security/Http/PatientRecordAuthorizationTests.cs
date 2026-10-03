using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SCI_PH.Application.Common.Security;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Infrastructure.Security;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.IntegrationTests.Security.Http
{
    // Exercises the real ASP.NET Core authorization handler and the clinical link in the Web host.
    public sealed class PatientRecordAuthorizationTests : IClassFixture<WebBffFactory>
    {
        private readonly WebBffFactory _factory;

        public PatientRecordAuthorizationTests(WebBffFactory factory) => _factory = factory;

        [Fact]
        public async Task PatientReadsOnlyTheirOwnRecord()
        {
            var userA = Guid.NewGuid();
            var patientA = await CreatePatientAsync(userA);
            var patientB = await CreatePatientAsync(Guid.NewGuid());
            var principal = Principal(userA, [SecurityRoles.Patient], []);

            Assert.True(await AuthorizeAsync(principal, patientA, PatientRecordRequirement.Read(Permissions.Patients.Read)));
            Assert.True(await AuthorizeAsync(principal, patientA, PatientRecordRequirement.Read(Permissions.Documents.Read)));
            Assert.False(await AuthorizeAsync(principal, patientB, PatientRecordRequirement.Read(Permissions.Patients.Read)));
            Assert.False(await AuthorizeAsync(principal, patientA, PatientRecordRequirement.Write(Permissions.Patients.Update)));
            Assert.False(await AuthorizeAsync(principal, patientA, PatientRecordRequirement.Read(Permissions.Audit.Read)));
        }

        [Fact]
        public async Task StaffAccessDependsOnThePermission()
        {
            var patient = await CreatePatientAsync(Guid.NewGuid());
            var doctor = Principal(Guid.NewGuid(), [SecurityRoles.Doctor], RolePermissions.For(SecurityRoles.Doctor));
            var receptionist = Principal(Guid.NewGuid(), [SecurityRoles.Receptionist], RolePermissions.For(SecurityRoles.Receptionist));

            Assert.True(await AuthorizeAsync(doctor, patient, PatientRecordRequirement.Read(Permissions.Encounters.Read)));
            Assert.True(await AuthorizeAsync(doctor, patient, PatientRecordRequirement.Write(Permissions.Diagnoses.Write)));
            Assert.False(await AuthorizeAsync(receptionist, patient, PatientRecordRequirement.Read(Permissions.Encounters.Read)));
        }

        private async Task<Guid> CreatePatientAsync(Guid identityUserId)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var clinical = scope.ServiceProvider.GetRequiredService<ClinicalDbContext>();
            var patient = Patient.Register("Luis", "Gómez", null, BiologicalSex.Male);
            patient.LinkIdentityUser(identityUserId);
            clinical.Patients.Add(patient);
            await clinical.SaveChangesAsync();
            return patient.Id;
        }

        private async Task<bool> AuthorizeAsync(ClaimsPrincipal principal, Guid patientId, PatientRecordRequirement requirement)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
            {
                User = principal,
                RequestServices = scope.ServiceProvider
            };
            var result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, patientId, requirement);
            return result.Succeeded;
        }

        private static ClaimsPrincipal Principal(Guid userId, IEnumerable<string> roles, IEnumerable<string> permissions)
        {
            var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, userId.ToString()) };
            claims.AddRange(roles.Select(role => new Claim(CustomClaimTypes.Role, role)));
            claims.AddRange(permissions.Select(permission => new Claim(CustomClaimTypes.Permission, permission)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", CustomClaimTypes.UserName, CustomClaimTypes.Role));
        }
    }
}
