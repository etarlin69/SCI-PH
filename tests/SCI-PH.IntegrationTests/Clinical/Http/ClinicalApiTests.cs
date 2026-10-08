using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // The clinical vertical through the real Web BFF over PostgreSQL: permissions, clinic isolation, ProblemDetails
    // and the JSON contract (enums as text).
    [Collection(PostgresCollection.Name)]
    public sealed class ClinicalApiTests
    {
        private const string Encounters = "/api/web/encounters";
        private const string Admissions = "/api/web/admissions/encounters";
        private readonly PostgresFixture _postgres;

        public ClinicalApiTests(PostgresFixture postgres) => _postgres = postgres;

        [SkippableFact]
        public async Task EveryClinicalEndpointRequiresAToken()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var anonymous = host.Anonymous();
            var id = Guid.NewGuid();

            foreach (var response in new[]
            {
                await anonymous.GetAsync(Encounters),
                await anonymous.GetAsync($"{Encounters}/{id}"),
                await anonymous.PostAsJsonAsync(Encounters, new { type = "Emergency" }),
                await anonymous.PostAsJsonAsync($"{Encounters}/{id}/triages", new { priority = "Urgent" }),
                await anonymous.GetAsync(Admissions),
                await anonymous.PostAsJsonAsync("/api/web/patients/search", new { value = "Ana" }),
                await anonymous.GetAsync("/api/web/clinical/context")
            })
            {
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }

        [SkippableFact]
        public async Task ReceptionistOpensEncountersButNeverGetsClinicalReads()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var patient = await host.SeedPatientAsync();

            var created = await reception.Client.PostAsJsonAsync(Encounters, new { type = "Emergency", patientId = patient.Id, reason = "Dolor torácico" });

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var encounter = await created.ReadAsAsync<EncounterSummary>();
            Assert.Equal(ClinicalEncounterStatus.Created, encounter.Status);
            Assert.Equal(clinic.Id, encounter.ClinicId);
            Assert.EndsWith($"{Encounters}/{encounter.Id}", created.Headers.Location!.ToString());
            await AssertProblemAsync(await reception.Client.GetAsync(Encounters), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await reception.Client.GetAsync($"{Encounters}/{encounter.Id}"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await reception.Client.GetAsync($"{Encounters}/{encounter.Id}/triages"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await reception.Client.GetAsync($"{Encounters}/{encounter.Id}/vital-signs"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(
                await reception.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/triages", new { priority = "Urgent" }), HttpStatusCode.Forbidden);
        }

        [SkippableFact]
        public async Task AdmissionProjectionLocatesNnEncountersWithoutLeakingClinicalData()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var nurse = await host.SignInAsync(SecurityRoles.Nurse, [clinic], staffType: ClinicalStaffType.Nurse);
            var nn = await CreateEncounterAsync(reception.Client, new { type = "Emergency", reason = "Motivo clínico sensible" });
            var triage = await nurse.Client.PostAsJsonAsync($"{Encounters}/{nn.Id}/triages", new
            {
                priority = "Immediate",
                observations = "Observación clínica sensible",
                measurements = new[] { new { type = "HeartRate", value = 130, measuredAtUtc = DateTimeOffset.UtcNow } }
            });
            Assert.Equal(HttpStatusCode.Created, triage.StatusCode);

            var list = await reception.Client.GetAsync($"{Admissions}?unidentified=true");
            var single = await reception.Client.GetAsync($"{Admissions}/{nn.Id}");

            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Equal(HttpStatusCode.OK, single.StatusCode);
            var item = Assert.Single(await list.ReadAsAsync<List<AdmissionEncounterSummary>>());
            Assert.Equal(nn.TemporaryPatientCode, item.TemporaryPatientCode);
            Assert.Equal(ClinicalEncounterStatus.InProgress, item.Status);
            foreach (var body in new[] { await list.Content.ReadAsStringAsync(), await single.Content.ReadAsStringAsync() })
            {
                Assert.DoesNotContain("sensible", body);
                Assert.DoesNotContain("Immediate", body);
                Assert.DoesNotContain("HeartRate", body);
                foreach (var field in new[] { "reason", "latestTriagePriority", "observations", "vitalSigns", "notes", "diagnoses", "results", "prescriptions" })
                {
                    Assert.DoesNotContain($"\"{field}\"", body, StringComparison.OrdinalIgnoreCase);
                }
            }

            // Nurses have no Patients.Identify: the admission surface is not theirs.
            await AssertProblemAsync(await nurse.Client.GetAsync(Admissions), HttpStatusCode.Forbidden);
        }

        [SkippableFact]
        public async Task NurseRecordsTriageAndVitalSignsAndTheEncounterStarts()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var nurse = await host.SignInAsync(SecurityRoles.Nurse, [clinic], staffType: ClinicalStaffType.Nurse);
            var encounter = await CreateEncounterAsync(reception.Client, new { type = "Emergency", patientId = (await host.SeedPatientAsync()).Id });

            var triage = await nurse.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/triages", new
            {
                priority = "VeryUrgent",
                reason = "Disnea",
                measurements = new object[]
                {
                    new { type = "RespiratoryRate", value = 28, measuredAtUtc = DateTimeOffset.UtcNow },
                    new { type = "OxygenSaturation", value = 89, measuredAtUtc = DateTimeOffset.UtcNow }
                }
            });
            var vital = await nurse.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/vital-signs",
                new { type = "Temperature", value = 38.4, measuredAtUtc = DateTimeOffset.UtcNow });

            Assert.Equal(HttpStatusCode.Created, triage.StatusCode);
            Assert.Equal(HttpStatusCode.Created, vital.StatusCode);
            var recorded = await triage.ReadAsAsync<TriageSummary>();
            var triageId = recorded.Id;
            Assert.Equal(nurse.StaffId, recorded.PerformedByStaffId);
            var vitals = await (await nurse.Client.GetAsync($"{Encounters}/{encounter.Id}/vital-signs")).ReadAsAsync<List<VitalSignSummary>>();
            Assert.Equal(3, vitals.Count);
            Assert.Equal(2, vitals.Count(v => v.TriageId == triageId));
            Assert.Equal("Cel", vitals.Single(v => v.Type == VitalSignType.Temperature).Unit);
            var current = await (await nurse.Client.GetAsync($"{Encounters}/{encounter.Id}")).ReadAsAsync<EncounterSummary>();
            Assert.Equal(ClinicalEncounterStatus.InProgress, current.Status);
            Assert.Equal(TriagePriority.VeryUrgent, current.LatestTriagePriority);
            Assert.Equal(2, await host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.ClinicalEncounterId == encounter.Id && a.Action == AuditActionType.Create && a.ActorStaffId == nurse.StaffId)));
        }

        [SkippableFact]
        public async Task ClinicalResponsesUseEnumNamesAndRejectNumbersAndUnknownFields()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            var encounter = await CreateEncounterAsync(doctor.Client, new { type = "Emergency" });
            await doctor.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/vital-signs",
                new { type = "HeartRate", value = 72, measuredAtUtc = DateTimeOffset.UtcNow });

            using var json = JsonDocument.Parse(await doctor.Client.GetStringAsync($"{Encounters}/{encounter.Id}"));
            using var vitals = JsonDocument.Parse(await doctor.Client.GetStringAsync($"{Encounters}/{encounter.Id}/vital-signs"));

            Assert.Equal("Emergency", json.RootElement.GetProperty("type").GetString());
            Assert.Equal("InProgress", json.RootElement.GetProperty("status").GetString());
            Assert.Equal("HeartRate", vitals.RootElement[0].GetProperty("type").GetString());
            await AssertProblemAsync(await doctor.Client.PostAsJsonAsync(Encounters, new { type = 2 }), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await doctor.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/vital-signs",
                new { type = "HeartRate", value = 72, unit = "/min", measuredAtUtc = DateTimeOffset.UtcNow }), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await doctor.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/vital-signs",
                new { type = "HeartRate", measuredAtUtc = DateTimeOffset.UtcNow }), HttpStatusCode.BadRequest);

            // Auth/Admin contracts are untouched by the clinical converter.
            using var me = JsonDocument.Parse(await doctor.Client.GetStringAsync("/api/web/auth/me"));
            Assert.Equal(JsonValueKind.Array, me.RootElement.GetProperty("roles").ValueKind);
        }

        [SkippableFact]
        public async Task ValidationNotFoundAndConflictAreProblemDetails()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            var patient = await host.SeedPatientAsync();
            var nn = await CreateEncounterAsync(doctor.Client, new { type = "Emergency" });

            var nnConsultation = await AssertProblemAsync(
                await doctor.Client.PostAsJsonAsync(Encounters, new { type = "Consultation" }), HttpStatusCode.BadRequest);
            Assert.Equal("patientId", nnConsultation.GetProperty("field").GetString());
            await AssertProblemAsync(await doctor.Client.PostAsJsonAsync(Encounters, new { type = "Prehospital" }), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await doctor.Client.PostAsJsonAsync($"{Encounters}/{nn.Id}/vital-signs",
                new { type = "HeartRate", value = 72, measuredAtUtc = DateTimeOffset.UtcNow.AddHours(1) }), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await doctor.Client.PostAsJsonAsync($"{Encounters}/{nn.Id}/vital-signs",
                new { type = "HeartRate", value = 72, measuredAtUtc = nn.StartedAtUtc.AddMinutes(-5) }), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await doctor.Client.PostAsJsonAsync($"{Encounters}/{nn.Id}/vital-signs",
                new { type = "HeartRate", value = 400, measuredAtUtc = DateTimeOffset.UtcNow }), HttpStatusCode.BadRequest);

            await AssertProblemAsync(await doctor.Client.GetAsync($"{Encounters}/{Guid.NewGuid()}"), HttpStatusCode.NotFound);
            await AssertProblemAsync(await doctor.Client.PostAsJsonAsync(Encounters, new { type = "Emergency", patientId = Guid.NewGuid() }), HttpStatusCode.NotFound);
            await AssertProblemAsync(
                await doctor.Client.PostAsJsonAsync($"{Encounters}/{nn.Id}/identify", new { patientId = Guid.NewGuid() }), HttpStatusCode.NotFound);

            Assert.Equal(HttpStatusCode.NoContent,
                (await doctor.Client.PostAsJsonAsync($"{Encounters}/{nn.Id}/identify", new { patientId = patient.Id })).StatusCode);
            await AssertProblemAsync(
                await doctor.Client.PostAsJsonAsync($"{Encounters}/{nn.Id}/identify", new { patientId = patient.Id }), HttpStatusCode.Conflict);

            await host.DbAsync(async db =>
            {
                var encounter = await db.ClinicalEncounters.SingleAsync(e => e.Id == nn.Id);
                encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, DateTimeOffset.UtcNow);
                encounter.ChangeStatus(ClinicalEncounterStatus.Closed, DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
            });
            await AssertProblemAsync(
                await doctor.Client.PostAsJsonAsync($"{Encounters}/{nn.Id}/triages", new { priority = "Standard" }), HttpStatusCode.Conflict);
        }

        [SkippableFact]
        public async Task ClinicsAreIsolatedAndAForeignClinicIdIsForbidden()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var (clinicA, clinicB) = (await host.CreateClinicAsync("Hospital A"), await host.CreateClinicAsync("Hospital B"));
            var doctorA = await host.SignInAsync(SecurityRoles.Doctor, [clinicA], staffType: ClinicalStaffType.Physician);
            var nurseB = await host.SignInAsync(SecurityRoles.Nurse, [clinicB], staffType: ClinicalStaffType.Nurse);
            var receptionB = await host.SignInAsync(SecurityRoles.Receptionist, [clinicB]);
            var both = await host.SignInAsync(SecurityRoles.Doctor, [clinicA, clinicB], staffType: ClinicalStaffType.Physician);
            var encounterA = await CreateEncounterAsync(doctorA.Client, new { type = "Emergency" });

            await AssertProblemAsync(await nurseB.Client.GetAsync($"{Encounters}/{encounterA.Id}"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await nurseB.Client.PostAsJsonAsync($"{Encounters}/{encounterA.Id}/vital-signs",
                new { type = "HeartRate", value = 72, measuredAtUtc = DateTimeOffset.UtcNow }), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await receptionB.Client.GetAsync($"{Admissions}/{encounterA.Id}"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(
                await receptionB.Client.PostAsJsonAsync($"{Encounters}/{encounterA.Id}/identify", new { patientId = (await host.SeedPatientAsync()).Id }),
                HttpStatusCode.Forbidden);
            Assert.Empty(await (await nurseB.Client.GetAsync(Encounters)).ReadAsAsync<List<EncounterSummary>>());
            Assert.Empty(await (await receptionB.Client.GetAsync(Admissions)).ReadAsAsync<List<AdmissionEncounterSummary>>());
            await AssertProblemAsync(await nurseB.Client.GetAsync($"{Encounters}?clinicId={clinicA.Id}"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(
                await doctorA.Client.PostAsJsonAsync(Encounters, new { type = "Emergency", clinicId = clinicB.Id }), HttpStatusCode.Forbidden);

            // Several affiliations: clinicId is required, then the list is scoped to it.
            await AssertProblemAsync(await both.Client.GetAsync(Encounters), HttpStatusCode.BadRequest);
            Assert.Single(await (await both.Client.GetAsync($"{Encounters}?clinicId={clinicA.Id}")).ReadAsAsync<List<EncounterSummary>>());
            Assert.Empty(await (await both.Client.GetAsync($"{Encounters}?clinicId={clinicB.Id}")).ReadAsAsync<List<EncounterSummary>>());
            var context = await (await both.Client.GetAsync("/api/web/clinical/context")).ReadAsAsync<ClinicalContextSummary>();
            Assert.Equal(2, context.Clinics.Count);
        }

        [SkippableFact]
        public async Task PermissionsAndClinicalStaffAreBothRequired()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var nurse = await host.SignInAsync(SecurityRoles.Nurse, [clinic], staffType: ClinicalStaffType.Nurse);
            var adminWithoutStaff = await host.SignInAsync(SecurityRoles.Administrator, [], asClinicalStaff: false);
            var inactiveDoctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffActive: false);
            var doctorWithoutClinic = await host.SignInAsync(SecurityRoles.Doctor, []);
            var encounter = await CreateEncounterAsync((await host.SignInAsync(SecurityRoles.Receptionist, [clinic])).Client, new { type = "Emergency" });

            // Nurse: no Encounters.Create and no Patients.Identify in RolePermissions.
            await AssertProblemAsync(await nurse.Client.PostAsJsonAsync(Encounters, new { type = "Emergency" }), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await nurse.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/identify",
                new { patientId = (await host.SeedPatientAsync()).Id }), HttpStatusCode.Forbidden);
            // Administrator has every permission but no clinical bypass.
            await AssertProblemAsync(await adminWithoutStaff.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/triages",
                new { priority = "Urgent" }), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await adminWithoutStaff.Client.GetAsync(Encounters), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await inactiveDoctor.Client.GetAsync(Encounters), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await doctorWithoutClinic.Client.GetAsync(Encounters), HttpStatusCode.Forbidden);
            Assert.Equal(HttpStatusCode.OK, (await nurse.Client.GetAsync(Encounters)).StatusCode);
        }

        [SkippableFact]
        public async Task PatientRegistrationAndSearchKeepIdentifiersOutOfUrlsAndMasked()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            var register = new
            {
                givenNames = "María José",
                familyNames = "Rodríguez Peña",
                dateOfBirth = "1992-07-21",
                sex = "Female",
                identifier = new { type = "NationalId", value = "  031-0456789-1 " }
            };

            var created = await reception.Client.PostAsJsonAsync("/api/web/patients", register);
            var duplicate = await reception.Client.PostAsJsonAsync("/api/web/patients", register with { });
            var byId = await reception.Client.PostAsJsonAsync("/api/web/patients/search", new { identifierType = "NationalId", value = "03104567891" });
            var byName = await reception.Client.PostAsJsonAsync("/api/web/patients/search", new { value = "josé rodr" });

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var patient = await created.ReadAsAsync<PatientSummary>();
            Assert.Equal("***7891", patient.PrimaryIdentifier!.Value);
            Assert.DoesNotContain("0456789", created.Headers.Location!.ToString());
            Assert.EndsWith($"/api/web/patients/{patient.Id}", created.Headers.Location!.ToString());
            Assert.Equal("03104567891", await host.DbAsync(db => db.PatientIdentifiers.Where(i => i.PatientId == patient.Id).Select(i => i.NormalizedValue).SingleAsync()));
            await AssertProblemAsync(duplicate, HttpStatusCode.Conflict);
            Assert.Equal(patient.Id, Assert.Single(await byId.ReadAsAsync<List<PatientSummary>>()).Id);
            Assert.Equal(patient.Id, Assert.Single(await byName.ReadAsAsync<List<PatientSummary>>()).Id);
            Assert.DoesNotContain("0456789", await byId.Content.ReadAsStringAsync());
            await AssertProblemAsync(await reception.Client.PostAsJsonAsync("/api/web/patients/search", new { value = "jo" }), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await reception.Client.PostAsJsonAsync("/api/web/patients/search", new { identifierType = "NationalId", value = "--" }), HttpStatusCode.BadRequest);
            // Searching through the query string is not an API: identifiers only travel in bodies.
            Assert.NotEqual(HttpStatusCode.OK, (await reception.Client.GetAsync("/api/web/patients/search?value=03104567891")).StatusCode);
            // Doctors read patients but have no Patients.Create.
            await AssertProblemAsync(await doctor.Client.PostAsJsonAsync("/api/web/patients", register), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await reception.Client.PostAsJsonAsync("/api/web/patients",
                register with { dateOfBirth = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd") }), HttpStatusCode.BadRequest);
        }

        [SkippableFact]
        public async Task ReceptionIdentifiesAnNnEncounterKeepingTheTemporaryCodeAndAuditingIt()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var patient = await host.SeedPatientAsync();
            var nn = await CreateEncounterAsync(reception.Client, new { type = "Emergency" });

            var identify = await reception.Client.PostAsJsonAsync($"{Encounters}/{nn.Id}/identify", new { patientId = patient.Id });

            Assert.Equal(HttpStatusCode.NoContent, identify.StatusCode);
            var admission = await (await reception.Client.GetAsync($"{Admissions}/{nn.Id}")).ReadAsAsync<AdmissionEncounterSummary>();
            Assert.Equal(patient.Id, admission.PatientId);
            Assert.Equal(nn.TemporaryPatientCode, admission.TemporaryPatientCode);
            Assert.Equal("Ana Pérez", admission.PatientDisplayName);
            var stored = await host.DbAsync(db => db.ClinicalEncounters.SingleAsync(e => e.Id == nn.Id));
            Assert.Equal(reception.StaffId, stored.IdentifiedByStaffId);
            Assert.Equal(nn.TemporaryPatientCode, stored.TemporaryPatientCode!.Value);
            var audit = await host.DbAsync(db => db.ClinicalAuditEntries.SingleAsync(a => a.ClinicalEncounterId == nn.Id && a.Action == AuditActionType.Identify));
            Assert.Equal(reception.UserId, audit.ActorIdentityUserId);
            Assert.Equal(reception.StaffId, audit.ActorStaffId);
            Assert.Equal(patient.Id, audit.PatientId);
            Assert.Contains(nn.TemporaryPatientCode!, audit.ChangesJson);
        }

        [SkippableFact]
        public async Task ConcurrentIdentificationsYieldOneSuccessAndConflicts()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            var patients = new[] { await host.SeedPatientAsync("Uno", "Prueba"), await host.SeedPatientAsync("Dos", "Prueba") };

            for (var round = 0; round < 3; round++)
            {
                var nn = await CreateEncounterAsync(reception.Client, new { type = "Emergency" });
                using var start = new ManualResetEventSlim();
                var requests = new[] { (reception.Client, patients[0].Id), (doctor.Client, patients[1].Id) }
                    .Select(pair => Task.Run(async () =>
                    {
                        start.Wait();
                        return await pair.Item1.PostAsJsonAsync($"{Encounters}/{nn.Id}/identify", new { patientId = pair.Item2 });
                    }))
                    .ToArray();
                start.Set();
                var responses = await Task.WhenAll(requests);

                Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
                var loser = Assert.Single(responses, r => r.StatusCode != HttpStatusCode.NoContent);
                await AssertProblemAsync(loser, HttpStatusCode.Conflict);
                Assert.Equal(1, await host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == nn.Id && a.Action == AuditActionType.Identify)));
            }
        }

        private static async Task<EncounterSummary> CreateEncounterAsync(HttpClient client, object request)
        {
            var response = await client.PostAsJsonAsync(Encounters, request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<EncounterSummary>();
        }

        private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
            Assert.True(problem.TryGetProperty("title", out _));
            return problem;
        }
    }
}
