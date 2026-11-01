using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.Security.Http;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Mobile.BFF.Controllers;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Patient self-service (ADR-015) through the real Mobile BFF: two real Patient accounts linked to two patients. The
    // server derives the record from the account; nothing a client sends (ids in the route or the query) can reach
    // another patient, staff accounts never get in, a patient never gets into the paramedic API, and it is read-only.
    public sealed class PatientSelfServiceApiTests : IClassFixture<MobileBffFactory>
    {
        private const string Me = "/api/mobile/patient/me";
        private readonly MobileBffFactory _factory;

        public PatientSelfServiceApiTests(MobileBffFactory factory) => _factory = factory;

        private static readonly string[] SelfRoutes =
            [Me, $"{Me}/summary", $"{Me}/encounters", $"{Me}/allergies", $"{Me}/diagnoses", $"{Me}/prescriptions", $"{Me}/results"];

        [Fact]
        public async Task EachPatientReadsOnlyTheirOwnRecord()
        {
            var (a, clientA) = await SignInPatientAsync("Ana");
            var (b, clientB) = await SignInPatientAsync("Berta");

            await AssertOwnRecordAsync(clientA, a, b);
            await AssertOwnRecordAsync(clientB, b, a);
        }

        [Fact]
        public async Task AnotherPatientsEncounterIsNotFoundLikeAMissingOne()
        {
            var (a, clientA) = await SignInPatientAsync("Ana");
            var (b, _) = await SignInPatientAsync("Berta");

            var own = await clientA.GetAsync($"{Me}/encounters/{a.EncounterId}");
            var foreign = await clientA.GetAsync($"{Me}/encounters/{b.EncounterId}");
            var missing = await clientA.GetAsync($"{Me}/encounters/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            var foreignBody = await foreign.Content.ReadAsStringAsync();
            Assert.DoesNotContain(b.GivenNames, foreignBody);
            Assert.DoesNotContain(b.DiagnosisId.ToString(), foreignBody);

            // The detail holds only what was recorded in that own encounter, with the same visibility rules.
            var detail = await own.ReadAsAsync<PatientSelfEncounterDetail>();
            Assert.Equal(a.EncounterId, detail.Encounter.Id);
            Assert.Equal(a.DiagnosisId, Assert.Single(detail.Diagnoses).Id);
            Assert.Equal(a.FinalResultId, Assert.Single(detail.Results).Id);
            Assert.Equal(a.IssuedPrescriptionId, Assert.Single(detail.Prescriptions).Id);
            var older = await clientA.GetFromJsonAsync<PatientSelfEncounterDetail>($"{Me}/encounters/{a.OlderEncounterId}");
            Assert.Empty(older!.Diagnoses);
            Assert.Empty(older.Results);
            Assert.Empty(older.Prescriptions);
        }

        [Fact]
        public async Task APatientIdInTheQueryOrAnUnknownRouteNeverSelectsAnotherRecord()
        {
            var (a, clientA) = await SignInPatientAsync("Ana");
            var (b, _) = await SignInPatientAsync("Berta");

            foreach (var route in SelfRoutes)
            {
                var response = await clientA.GetAsync($"{route}?patientId={b.PatientId}&encounterId={b.EncounterId}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(b.GivenNames, body);
                Assert.DoesNotContain(b.EncounterId.ToString(), body);
            }

            Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"/api/mobile/patient/{b.PatientId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"{Me}/{b.PatientId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"{Me}/diagnoses/{b.DiagnosisId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"{Me}/results/{b.FinalResultId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"{Me}/prescriptions/{b.IssuedPrescriptionId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"{Me}/allergies/{b.AllergyId}")).StatusCode);
        }

        [Fact]
        public async Task ResponsesCarryNoInternalIdentifiers()
        {
            var (a, client) = await SignInPatientAsync("Ana");

            using var profile = JsonDocument.Parse(await client.GetStringAsync(Me));
            var root = profile.RootElement;
            Assert.Equal(a.GivenNames, root.GetProperty("givenNames").GetString());
            Assert.Equal("NationalId", root.GetProperty("document").GetProperty("type").GetString());
            Assert.StartsWith("***", root.GetProperty("document").GetProperty("value").GetString());
            Assert.EndsWith("@sci-ph.test", root.GetProperty("email").GetString());
            foreach (var name in new[] { "id", "patientId", "identityUserId", "identifiers" })
            {
                Assert.False(root.TryGetProperty(name, out _), name);
            }

            foreach (var route in SelfRoutes.Append($"{Me}/encounters/{a.EncounterId}"))
            {
                var body = await client.GetStringAsync(route);
                Assert.DoesNotContain(a.PatientId.ToString(), body);
                foreach (var name in new[] { "patientId", "identityUserId", "clinicId", "staffId", "blobPath", "subjectReference", "medicationId" })
                {
                    Assert.DoesNotContain(name, body, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        [Fact]
        public async Task APatientAccountWithoutClinicalLinkFailsClosedAndIsNeverLinkedByEmail()
        {
            var account = await _factory.CreateUserAsync(SecurityRoles.Patient);
            Guid patientId;
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                // Same email as the account: a match must never be used to link or to read.
                var db = scope.ServiceProvider.GetRequiredService<ClinicalDbContext>();
                patientId = (await PatientRecordSeed.SeedAsync(db, "Sinvinculo", identityUserId: null, email: account.Email)).PatientId;
            }

            var client = await LoginAsync(account);
            foreach (var route in SelfRoutes)
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
            }

            await using var check = _factory.Services.CreateAsyncScope();
            var stored = await check.ServiceProvider.GetRequiredService<ClinicalDbContext>().Patients.AsNoTracking().SingleAsync(p => p.Id == patientId);
            Assert.Null(stored.IdentityUserId);
        }

        [Theory]
        [InlineData(SecurityRoles.Paramedic)]
        [InlineData(SecurityRoles.Doctor)]
        [InlineData(SecurityRoles.Nurse)]
        [InlineData(SecurityRoles.Receptionist)]
        [InlineData(SecurityRoles.Administrator)]
        public async Task StaffAccountsDoNotBecomeAPatient(string role)
        {
            var (a, _) = await SignInPatientAsync("Ana");
            var staff = await LoginAsync(await _factory.CreateUserAsync(role));

            foreach (var route in SelfRoutes.Append($"{Me}/encounters/{a.EncounterId}"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(route)).StatusCode);
            }
        }

        [Fact]
        public async Task APatientCannotUseTheParamedicApi()
        {
            var (a, client) = await SignInPatientAsync("Ana");
            var emergency = new { clientRequestId = Guid.NewGuid(), startedAt = DateTimeOffset.UtcNow, chiefComplaint = "Dolor" };

            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/mobile/emergencies", emergency)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/mobile/emergencies/{a.EncounterId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/mobile/devices/fingerprint-readers")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/mobile/catalogs/medications")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
                $"/api/mobile/emergencies/{a.EncounterId}/identify/fingerprint/sessions", new { clientRequestId = Guid.NewGuid() })).StatusCode);
        }

        [Fact]
        public async Task TheSelfServiceIsReadOnlyAndNeedsAToken()
        {
            var (a, client) = await SignInPatientAsync("Ana");

            foreach (var route in SelfRoutes.Append($"{Me}/encounters/{a.EncounterId}"))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateHttpsClient().GetAsync(route)).StatusCode);
                Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync(route, new { })).StatusCode);
                Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsJsonAsync(route, new { })).StatusCode);
                Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
            }
        }

        private static async Task AssertOwnRecordAsync(HttpClient client, SeededPatientRecord own, SeededPatientRecord other)
        {
            var profile = await client.GetFromJsonAsync<PatientSelfProfile>(Me);
            Assert.Equal((own.GivenNames, "Prueba"), (profile!.GivenNames, profile.FamilyNames));

            var summary = await client.GetFromJsonAsync<PatientSelfSummary>($"{Me}/summary");
            Assert.Equal($"{own.GivenNames} Prueba", summary!.DisplayName);
            Assert.Equal((2, 1), (summary.EncounterCount, summary.ResultCount));
            Assert.Equal(own.EncounterId, summary.LatestEncounter!.Id);
            Assert.Equal(own.FinalResultId, summary.LatestResult!.Id);
            Assert.Equal(own.AllergyId, Assert.Single(summary.ActiveAllergies).Id);

            var encounters = (await client.GetFromJsonAsync<List<PatientSelfEncounter>>($"{Me}/encounters"))!;
            Assert.Equal([own.EncounterId, own.OlderEncounterId], encounters.Select(e => e.Id));
            Assert.Equal($"Hospital {own.GivenNames}", encounters[0].ClinicName);
            Assert.Equal((ClinicalEncounterType.Consultation, $"Fiebre {own.GivenNames}"), (encounters[0].Type, encounters[0].Reason));

            // Only the latest medical history version.
            var allergy = Assert.Single((await client.GetFromJsonAsync<List<PatientSelfAllergy>>($"{Me}/allergies"))!);
            Assert.Equal((own.AllergyId, $"Penicilina {own.GivenNames}", "Urticaria"), (allergy.Id, allergy.Substance, allergy.Reaction));

            var diagnosis = Assert.Single((await client.GetFromJsonAsync<List<PatientSelfDiagnosis>>($"{Me}/diagnoses"))!);
            Assert.Equal((own.DiagnosisId, own.EncounterId), (diagnosis.Id, diagnosis.EncounterId));

            // Issued only: never the draft nor the cancelled draft.
            var prescription = Assert.Single((await client.GetFromJsonAsync<List<PatientSelfPrescription>>($"{Me}/prescriptions"))!);
            Assert.Equal((own.IssuedPrescriptionId, PrescriptionStatus.Issued), (prescription.Id, prescription.Status));
            var item = Assert.Single(prescription.Items);
            Assert.Equal(($"Amoxicilina {own.GivenNames}", 500m, "Cada 8 horas"), (item.MedicationName, item.Dose, item.Frequency));

            // Published only: never the pending order.
            var result = Assert.Single((await client.GetFromJsonAsync<List<PatientSelfResult>>($"{Me}/results"))!);
            Assert.Equal((own.FinalResultId, ClinicalResultStatus.Final, 13.5m), (result.Id, result.Status, result.NumericValue));

            foreach (var route in SelfRoutes)
            {
                var body = await client.GetStringAsync(route);
                foreach (var foreignId in new[]
                {
                    other.PatientId, other.EncounterId, other.OlderEncounterId, other.DiagnosisId, other.AllergyId,
                    other.FinalResultId, other.IssuedPrescriptionId, own.SupersededAllergyId, own.PendingResultId,
                    own.DraftPrescriptionId, own.CancelledDraftPrescriptionId
                })
                {
                    Assert.DoesNotContain(foreignId.ToString(), body);
                }

                Assert.DoesNotContain(other.GivenNames, body);
            }
        }

        private async Task<(SeededPatientRecord Record, HttpClient Client)> SignInPatientAsync(string givenNames)
        {
            var account = await _factory.CreateUserAsync(SecurityRoles.Patient);
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                var record = await PatientRecordSeed.SeedAsync(
                    scope.ServiceProvider.GetRequiredService<ClinicalDbContext>(), $"{givenNames}{Guid.NewGuid():N}"[..(givenNames.Length + 6)], account.Id);
                return (record, await LoginAsync(account));
            }
        }

        private async Task<HttpClient> LoginAsync(TestAccount account)
        {
            var login = await _factory.CreateHttpsClient().PostAsJsonAsync("/api/mobile/auth/login", new LoginRequest(account.Email, account.Password));
            login.EnsureSuccessStatusCode();
            var session = (await login.ReadAsAsync<MobileLoginResponse>()).Session!;
            return _factory.CreateHttpsClient().WithBearer(session.AccessToken);
        }
    }
}
