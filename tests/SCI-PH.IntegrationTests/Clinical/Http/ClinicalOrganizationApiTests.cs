using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Clinics, clinical staff and affiliations administered through the real Web BFF over PostgreSQL, and the end-to-end
    // provisioning without seed: clinic -> staff -> affiliation -> staff invitation -> activation -> clinical actor.
    [Collection(PostgresCollection.Name)]
    public sealed class ClinicalOrganizationApiTests
    {
        private const string Clinics = "/api/web/admin/clinics";
        private const string Staff = "/api/web/admin/staff";
        private readonly PostgresFixture _postgres;

        public ClinicalOrganizationApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static object NewClinic(string name, string? facilityCode = null) =>
            new { name, type = "Hospital", province = "Distrito Nacional", municipality = "Santo Domingo", facilityCode, address = "Av. 1", phoneNumber = "809-555-0101" };

        private static object NewDoctor(string? license = null) =>
            new { givenNames = "Laura", familyNames = "Gómez", type = "Physician", professionalLicenseNumber = license ?? $"MED-{Guid.NewGuid():N}"[..14], specialty = "Medicina interna" };

        private static async Task<StaffSession> AdminAsync(ClinicalApiHost host) =>
            await host.SignInAsync(SecurityRoles.Administrator, [], asClinicalStaff: false);

        // A user whose only permissions are the given ones (a test role), so the policies are exercised directly.
        private static async Task<StaffSession> WithPermissionsAsync(ClinicalApiHost host, params string[] permissions)
        {
            var role = $"Test-{Guid.NewGuid():N}"[..20];
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                var created = new IdentityRole<Guid>(role);
                Assert.True((await roles.CreateAsync(created)).Succeeded);
                foreach (var permission in permissions)
                {
                    Assert.True((await roles.AddClaimAsync(created, new Claim(CustomClaimTypes.Permission, permission))).Succeeded);
                }
            }

            return await host.SignInAsync(role, [], asClinicalStaff: false);
        }

        private static async Task<T> CreatedAsync<T>(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<T>();
        }

        [SkippableFact]
        public async Task ClinicsAreRegisteredListedAndActivatedWithTheDomainFieldsOnly()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var admin = await AdminAsync(host);

            var response = await admin.Client.PostAsJsonAsync(Clinics, NewClinic("Hospital Quinta Vertical", "hqv-01"));
            var clinic = await CreatedAsync<ClinicAdminSummary>(response);
            Assert.EndsWith($"{Clinics}/{clinic.Id}", response.Headers.Location!.ToString());
            using (var json = JsonDocument.Parse(await admin.Client.GetStringAsync($"{Clinics}/{clinic.Id}")))
            {
                Assert.Equal("Hospital", json.RootElement.GetProperty("type").GetString());
                Assert.Equal("Active", json.RootElement.GetProperty("status").GetString());
                Assert.Equal("HQV-01", json.RootElement.GetProperty("facilityCode").GetString());
                Assert.False(json.RootElement.TryGetProperty("email", out _));
            }

            Assert.Contains(await (await admin.Client.GetAsync($"{Clinics}?search=quinta")).ReadAsAsync<List<ClinicAdminSummary>>(), c => c.Id == clinic.Id);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync(Clinics, NewClinic("Otro", "HQV-01")), HttpStatusCode.Conflict);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync(Clinics, NewClinic(" ")), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync(Clinics,
                new { name = "X", type = 1, province = "P", municipality = "M" }), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync(Clinics,
                new { name = "X", type = "Hospital", province = "P", municipality = "M", email = "x@y.z" }), HttpStatusCode.BadRequest);

            Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsync($"{Clinics}/{clinic.Id}/deactivate", null)).StatusCode);
            Assert.Contains(await (await admin.Client.GetAsync($"{Clinics}?status=Inactive")).ReadAsAsync<List<ClinicAdminSummary>>(), c => c.Id == clinic.Id);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsync($"{Clinics}/{clinic.Id}/activate", null)).StatusCode);
            Assert.Equal("Active", (await (await admin.Client.GetAsync($"{Clinics}/{clinic.Id}")).ReadAsAsync<ClinicAdminSummary>()).Status.ToString());
            await AssertStatusAsync(await admin.Client.GetAsync($"{Clinics}/{Guid.NewGuid()}"), HttpStatusCode.NotFound);
            await AssertStatusAsync(await admin.Client.PostAsync($"{Clinics}/{Guid.NewGuid()}/deactivate", null), HttpStatusCode.NotFound);
            Assert.Equal(2, await host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.EntityId == clinic.Id.ToString() && a.Action == AuditActionType.Update)));
        }

        // No seed: the clinic, the staff member, the affiliation and the invitation are created through the API; activating
        // the invitation links the account and the staff member becomes a clinical actor of that clinic.
        [SkippableFact]
        public async Task StaffIsProvisionedEndToEndWithoutSeedAndFollowsItsAffiliations()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var admin = await AdminAsync(host);
            var clinicA = await CreatedAsync<ClinicAdminSummary>(await admin.Client.PostAsJsonAsync(Clinics, NewClinic("Hospital A")));
            var clinicB = await CreatedAsync<ClinicAdminSummary>(await admin.Client.PostAsJsonAsync(Clinics, NewClinic("Hospital B")));
            var staff = await CreatedAsync<ClinicalStaffAdminSummary>(await admin.Client.PostAsJsonAsync(Staff, NewDoctor("MED-QV-001")));
            Assert.False(staff.AccountLinked);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync(Staff, NewDoctor("med-qv-001")), HttpStatusCode.Conflict);

            // Affiliation A.
            var affiliationA = await CreatedAsync<ClinicStaffAffiliationSummary>(
                await admin.Client.PostAsJsonAsync($"{Staff}/{staff.Id}/affiliations", new { clinicId = clinicA.Id }));
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync($"{Staff}/{staff.Id}/affiliations", new { clinicId = clinicA.Id }), HttpStatusCode.Conflict);

            // Invitation (existing endpoint, Users.Manage) -> real email -> activation -> account linked.
            var email = $"doctora-{Guid.NewGuid():N}"[..20] + "@sci-ph.test";
            Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsJsonAsync("/api/web/admin/invitations/staff",
                new { clinicalStaffId = staff.Id, email, role = "Doctor" })).StatusCode);
            var token = FakeEmailSender.LinkParameter(host.Factory.Email.LastTo(email), "token");
            var password = TestSettings.NewPassword();
            Assert.Equal(HttpStatusCode.NoContent, (await host.Anonymous().PostAsJsonAsync("/api/web/auth/activate-account", new { token, password })).StatusCode);
            var linked = await (await admin.Client.GetAsync($"{Staff}/{staff.Id}")).ReadAsAsync<ClinicalStaffAdminSummary>();
            Assert.True(linked.AccountLinked);
            Assert.NotNull(linked.IdentityUserId);
            Assert.Equal(["Hospital A"], linked.ActiveClinics.Select(c => c.Name));
            // A linked staff member cannot be invited again.
            Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsJsonAsync("/api/web/admin/invitations/staff",
                new { clinicalStaffId = staff.Id, email = "otra@sci-ph.test", role = "Doctor" })).StatusCode);

            // The new doctor acts clinically in clinic A.
            var doctor = await host.LoginAsync(email, password);
            var context = await (await doctor.GetAsync("/api/web/clinical/context")).ReadAsAsync<ClinicalContextSummary>();
            Assert.Equal((staff.Id, clinicA.Id), (context.StaffId, Assert.Single(context.Clinics).Id));
            var encounterA = await CreatedAsync<EncounterSummary>(await doctor.PostAsJsonAsync("/api/web/encounters", new { type = "Emergency" }));
            Assert.Equal(clinicA.Id, encounterA.ClinicId);

            // Multi-clinic: with a second open affiliation the clinic must be chosen.
            await CreatedAsync<ClinicStaffAffiliationSummary>(await admin.Client.PostAsJsonAsync($"{Staff}/{staff.Id}/affiliations", new { clinicId = clinicB.Id }));
            var both = await (await doctor.GetAsync("/api/web/clinical/context")).ReadAsAsync<ClinicalContextSummary>();
            Assert.Equal(new[] { clinicA.Id, clinicB.Id }.Order(), both.Clinics.Select(c => c.Id).Order());
            await AssertStatusAsync(await doctor.PostAsJsonAsync("/api/web/encounters", new { type = "Emergency" }), HttpStatusCode.BadRequest);
            Assert.Equal(clinicB.Id, (await CreatedAsync<EncounterSummary>(await doctor.PostAsJsonAsync("/api/web/encounters",
                new { type = "Emergency", clinicId = clinicB.Id }))).ClinicId);

            // Ending affiliation A keeps it as history; clinic A stops being a valid context.
            var ended = await (await admin.Client.PostAsync($"{Staff}/{staff.Id}/affiliations/{affiliationA.Id}/end", null)).ReadAsAsync<ClinicStaffAffiliationSummary>();
            Assert.False(ended.IsActive);
            Assert.NotNull(ended.EndedAtUtc);
            await AssertStatusAsync(await admin.Client.PostAsync($"{Staff}/{staff.Id}/affiliations/{affiliationA.Id}/end", null), HttpStatusCode.Conflict);
            var history = await (await admin.Client.GetAsync($"{Staff}/{staff.Id}/affiliations")).ReadAsAsync<List<ClinicStaffAffiliationSummary>>();
            Assert.Equal(2, history.Count);
            Assert.Single(history, a => !a.IsActive && a.ClinicId == clinicA.Id);
            Assert.Equal(clinicB.Id, Assert.Single((await (await doctor.GetAsync("/api/web/clinical/context")).ReadAsAsync<ClinicalContextSummary>()).Clinics).Id);
            await AssertStatusAsync(await doctor.GetAsync($"/api/web/encounters/{encounterA.Id}"), HttpStatusCode.Forbidden);

            // An inactive clinic is not a clinical context (its open affiliation is kept).
            Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsync($"{Clinics}/{clinicB.Id}/deactivate", null)).StatusCode);
            await AssertStatusAsync(await doctor.GetAsync("/api/web/clinical/context"), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync($"{Staff}/{staff.Id}/affiliations", new { clinicId = clinicB.Id }), HttpStatusCode.Conflict);
            var withInactiveClinic = await (await admin.Client.GetAsync($"{Staff}/{staff.Id}")).ReadAsAsync<ClinicalStaffAdminSummary>();
            Assert.Equal(clinicB.Id, Assert.Single(withInactiveClinic.ActiveClinics).Id);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsync($"{Clinics}/{clinicB.Id}/activate", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await doctor.GetAsync("/api/web/clinical/context")).StatusCode);

            // An inactive staff member keeps the account but is not a clinical actor and cannot be affiliated.
            Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsync($"{Staff}/{staff.Id}/deactivate", null)).StatusCode);
            await AssertStatusAsync(await doctor.GetAsync("/api/web/clinical/context"), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync($"{Staff}/{staff.Id}/affiliations", new { clinicId = clinicA.Id }), HttpStatusCode.Conflict);
            Assert.Equal(HttpStatusCode.OK, (await host.Anonymous().PostAsJsonAsync("/api/web/auth/login", new { identifier = email, password })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsync($"{Staff}/{staff.Id}/activate", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await doctor.GetAsync("/api/web/clinical/context")).StatusCode);
        }

        [SkippableFact]
        public async Task PermissionsArePoliciesNotRoles()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var admin = await AdminAsync(host);
            var clinic = await CreatedAsync<ClinicAdminSummary>(await admin.Client.PostAsJsonAsync(Clinics, NewClinic("Hospital Permisos")));
            var staff = await CreatedAsync<ClinicalStaffAdminSummary>(await admin.Client.PostAsJsonAsync(Staff, NewDoctor()));
            var anonymous = host.Anonymous();

            foreach (var response in new[]
            {
                await anonymous.GetAsync(Clinics),
                await anonymous.PostAsJsonAsync(Clinics, NewClinic("X")),
                await anonymous.PostAsync($"{Clinics}/{clinic.Id}/deactivate", null),
                await anonymous.GetAsync(Staff),
                await anonymous.PostAsJsonAsync(Staff, NewDoctor()),
                await anonymous.GetAsync($"{Staff}/{staff.Id}/affiliations"),
                await anonymous.PostAsJsonAsync($"{Staff}/{staff.Id}/affiliations", new { clinicId = clinic.Id })
            })
            {
                await AssertStatusAsync(response, HttpStatusCode.Unauthorized);
            }

            // A clinical user (real role matrix) administers nothing.
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [await host.DbAsync(db => db.Clinics.SingleAsync(c => c.Id == clinic.Id))]);
            await AssertStatusAsync(await doctor.Client.GetAsync(Clinics), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await doctor.Client.GetAsync(Staff), HttpStatusCode.Forbidden);

            // ClinicalStaff.Manage only: staff and affiliations, but no clinics and no invitations (Users.Manage).
            var staffManager = await WithPermissionsAsync(host, Permissions.ClinicalStaff.Manage);
            Assert.Equal(HttpStatusCode.OK, (await staffManager.Client.GetAsync($"{Staff}/{staff.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await staffManager.Client.PostAsJsonAsync($"{Staff}/{staff.Id}/affiliations", new { clinicId = clinic.Id })).StatusCode);
            await AssertStatusAsync(await staffManager.Client.GetAsync(Clinics), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await staffManager.Client.PostAsJsonAsync("/api/web/admin/invitations/staff",
                new { clinicalStaffId = staff.Id, email = "x@sci-ph.test", role = "Doctor" }), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await staffManager.Client.GetAsync("/api/web/admin/users"), HttpStatusCode.Forbidden);

            // Clinics.Manage only: clinics, but no staff.
            var clinicManager = await WithPermissionsAsync(host, Permissions.Clinics.Manage);
            Assert.Equal(HttpStatusCode.OK, (await clinicManager.Client.GetAsync(Clinics)).StatusCode);
            await AssertStatusAsync(await clinicManager.Client.GetAsync(Staff), HttpStatusCode.Forbidden);

            // Users.Manage only: invitations, but not clinics or staff.
            var userManager = await WithPermissionsAsync(host, Permissions.Users.Manage);
            await AssertStatusAsync(await userManager.Client.GetAsync(Clinics), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await userManager.Client.GetAsync(Staff), HttpStatusCode.Forbidden);
            Assert.Equal(HttpStatusCode.Created, (await userManager.Client.PostAsJsonAsync("/api/web/admin/invitations/staff",
                new { clinicalStaffId = staff.Id, email = $"inv-{Guid.NewGuid():N}"[..12] + "@sci-ph.test", role = "Doctor" })).StatusCode);
        }

        [SkippableFact]
        public async Task NotFoundAndValidationAreProblemDetails()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var admin = await AdminAsync(host);
            var staff = await CreatedAsync<ClinicalStaffAdminSummary>(await admin.Client.PostAsJsonAsync(Staff, NewDoctor()));
            var other = await CreatedAsync<ClinicalStaffAdminSummary>(await admin.Client.PostAsJsonAsync(Staff, NewDoctor()));
            var clinic = await CreatedAsync<ClinicAdminSummary>(await admin.Client.PostAsJsonAsync(Clinics, NewClinic("Hospital Errores")));
            var affiliation = await CreatedAsync<ClinicStaffAffiliationSummary>(
                await admin.Client.PostAsJsonAsync($"{Staff}/{staff.Id}/affiliations", new { clinicId = clinic.Id }));

            await AssertStatusAsync(await admin.Client.GetAsync($"{Staff}/{Guid.NewGuid()}"), HttpStatusCode.NotFound);
            await AssertStatusAsync(await admin.Client.GetAsync($"{Staff}/{Guid.NewGuid()}/affiliations"), HttpStatusCode.NotFound);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync($"{Staff}/{Guid.NewGuid()}/affiliations", new { clinicId = clinic.Id }), HttpStatusCode.NotFound);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync($"{Staff}/{staff.Id}/affiliations", new { clinicId = Guid.NewGuid() }), HttpStatusCode.NotFound);
            await AssertStatusAsync(await admin.Client.PostAsync($"{Staff}/{other.Id}/affiliations/{affiliation.Id}/end", null), HttpStatusCode.NotFound);
            await AssertStatusAsync(await admin.Client.PostAsync($"{Staff}/{Guid.NewGuid()}/activate", null), HttpStatusCode.NotFound);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync(Staff, new { givenNames = "", familyNames = "X", type = "Nurse" }), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync(Staff, new { givenNames = "A", familyNames = "B", type = "Nurse", email = "a@b.c" }), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync(Staff, new { givenNames = "A", familyNames = "B", type = "Doctor" }), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await admin.Client.PostAsJsonAsync($"{Staff}/{staff.Id}/affiliations", new { clinicId = clinic.Id, startedAtUtc = DateTimeOffset.UtcNow }), HttpStatusCode.BadRequest);
        }

        // Two concurrent requests to open the same staff + clinic affiliation: only one persists, the other gets a 409
        // (from the Application check or from UX_ClinicStaffAffiliations_Active).
        [SkippableFact]
        public async Task ConcurrentAffiliationsOfTheSameStaffAndClinicOnlyOneWins()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var admin = await AdminAsync(host);
            var staffManager = await WithPermissionsAsync(host, Permissions.ClinicalStaff.Manage);

            for (var round = 0; round < 4; round++)
            {
                var clinic = await CreatedAsync<ClinicAdminSummary>(await admin.Client.PostAsJsonAsync(Clinics, NewClinic($"Hospital Carrera {round}")));
                var staff = await CreatedAsync<ClinicalStaffAdminSummary>(await admin.Client.PostAsJsonAsync(Staff, NewDoctor()));
                using var start = new ManualResetEventSlim();
                Task<HttpResponseMessage> Run(HttpClient client) => Task.Run(async () =>
                {
                    start.Wait();
                    return await client.PostAsJsonAsync($"{Staff}/{staff.Id}/affiliations", new { clinicId = clinic.Id });
                });
                var tasks = new[] { Run(admin.Client), Run(staffManager.Client) };
                start.Set();
                var responses = await Task.WhenAll(tasks);

                Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
                await AssertStatusAsync(responses.Single(r => r.StatusCode != HttpStatusCode.Created), HttpStatusCode.Conflict);
                Assert.Equal(1, await host.DbAsync(db => db.ClinicStaffAffiliations.CountAsync(a => a.ClinicalStaffId == staff.Id && a.EndedAtUtc == null)));
            }
        }

        private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
        }
    }
}
