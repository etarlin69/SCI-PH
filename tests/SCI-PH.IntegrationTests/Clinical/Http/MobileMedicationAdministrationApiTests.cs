using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Clinical.Idempotency;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Medication administrations on a real prehospital emergency through the Mobile BFF over PostgreSQL: the active
    // catalog, one dose without prescription + its audit entry + the start of the encounter + the receipt in one
    // transaction; dose precision kept away from the numeric(12,3) column limits; replay, conflict and the race of the same
    // dose without duplicates or 500s; access by assignment and permission; the encounter's concurrency tokens against a
    // close, a dispatch and an identification; the Web path keeps the clinic rule.
    [Collection(PostgresCollection.Name)]
    public sealed class MobileMedicationAdministrationApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private const string Catalog = "/api/mobile/catalogs/medications";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly PostgresFixture _postgres;

        public MobileMedicationAdministrationApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static DateTimeOffset NowToTheMillisecond()
        {
            var now = DateTimeOffset.UtcNow;
            return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        }

        private static string Url(Guid emergencyId) => $"{Emergencies}/{emergencyId}/medication-administrations";

        private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv);

        private static string DoseJson(
            Guid key, Guid medicationId, string administeredAt, string dose = "500", string doseUnit = "mg", string route = "\"Oral\"",
            string? notes = "Con agua") =>
            $$"""{"clientRequestId":"{{key}}","medicationId":"{{medicationId}}","dose":{{dose}},"doseUnit":"{{doseUnit}}","route":{{route}},"administeredAt":"{{administeredAt}}"{{(notes is null ? "" : $",\"notes\":\"{notes}\"")}}}""";

        private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, Guid emergencyId, string json) =>
            client.PostAsync(Url(emergencyId), new StringContent(json, Encoding.UTF8, "application/json"));

        private static async Task<PrehospitalEmergencySummary> OpenAsync(StaffSession paramedic, DateTimeOffset startedAt)
        {
            var response = await paramedic.Client.PostAsJsonAsync(Emergencies, new { clientRequestId = Guid.NewGuid(), startedAt, chiefComplaint = "Dolor" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<PrehospitalEmergencySummary>();
        }

        private static async Task StartAsync(StaffSession paramedic, Guid emergencyId) =>
            Assert.Equal(HttpStatusCode.Created, (await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergencyId}/vital-signs",
                new { clientRequestId = Guid.NewGuid(), recordedAt = NowToTheMillisecond(), heartRate = 90 })).StatusCode);

        private static Task<int> AdministrationsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.MedicationAdministrations.CountAsync(a => a.ClinicalEncounterId == emergencyId));

        private static Task<int> AuditsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergencyId && a.EntityName == nameof(MedicationAdministration)));

        private static Task<int> ReceiptsAsync(MobileEmergencyHost host) =>
            host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == PrehospitalEmergencyService.RecordMedicationAdministrationOperation));

        private static Task<ClinicalEncounterStatus> StatusAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == emergencyId).Select(e => e.Status).SingleAsync());

        private static async Task<Medication> SeedAsync(MobileEmergencyHost host, Medication medication)
        {
            await host.Web.DbAsync(async db => { db.Medications.Add(medication); await db.SaveChangesAsync(); });
            return medication;
        }

        // A mobile user whose only permissions are the given ones (a test role), as active paramedic staff of the clinic.
        private static async Task<StaffSession> WithPermissionsAsync(MobileEmergencyHost host, Clinic clinic, params string[] permissions)
        {
            var role = $"Test-{Guid.NewGuid():N}"[..20];
            await using (var scope = host.Web.Services.CreateAsyncScope())
            {
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                var created = new IdentityRole<Guid>(role);
                Assert.True((await roles.CreateAsync(created)).Succeeded);
                foreach (var permission in permissions)
                {
                    Assert.True((await roles.AddClaimAsync(created, new Claim(CustomClaimTypes.Permission, permission))).Succeeded);
                }
            }

            return await host.SignInAsync(role, [clinic]);
        }

        [SkippableFact]
        public async Task TheCatalogListsOnlyActiveMedicationsInOrderBehindMedicationsRead()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var clinic = await host.Web.CreateClinicAsync();
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var paracetamol1g = await SeedAsync(host, Medication.Register("Paracetamol", "Tableta", "1 g", "T-PARA-1G", MedicationRoute.Oral));
            var paracetamol500 = await SeedAsync(host, Medication.Register("Paracetamol", "Tableta", "500 mg", "T-PARA-500", MedicationRoute.Oral));
            var metamizol = await SeedAsync(host, Medication.Register("Metamizol", "Solución inyectable", "1 g/2 mL", null, MedicationRoute.Intravenous));
            var noRoute = await SeedAsync(host, Medication.Register("Agua estéril"));
            var inactive = Medication.Register("Ibuprofeno", "Tableta", "400 mg", "T-IBU");
            inactive.Deactivate();
            await SeedAsync(host, inactive);

            var response = await paramedic.Client.GetAsync(Catalog);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = json.RootElement.EnumerateArray().ToList();
            Assert.Equal([noRoute.Id, metamizol.Id, paracetamol1g.Id, paracetamol500.Id], items.Select(i => i.GetProperty("id").GetGuid()));
            Assert.All(items, i => Assert.Equal(["id", "code", "name", "presentation", "concentration", "defaultRoute"], i.EnumerateObject().Select(p => p.Name)));
            var item = items.Single(i => i.GetProperty("id").GetGuid() == metamizol.Id);
            Assert.Equal(("Metamizol", "Solución inyectable", "1 g/2 mL", "Intravenous", JsonValueKind.Null),
                (item.GetProperty("name").GetString(), item.GetProperty("presentation").GetString(), item.GetProperty("concentration").GetString(),
                 item.GetProperty("defaultRoute").GetString(), item.GetProperty("code").ValueKind));
            Assert.Equal(JsonValueKind.Null, items.Single(i => i.GetProperty("id").GetGuid() == noRoute.Id).GetProperty("defaultRoute").ValueKind);

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Anonymous().GetAsync(Catalog)).StatusCode);
            var nurse = await host.SignInAsync(SecurityRoles.Nurse, [clinic], staffType: ClinicalStaffType.Nurse);
            await AssertProblemAsync(await nurse.Client.GetAsync(Catalog), HttpStatusCode.Forbidden);
            var withoutRead = await WithPermissionsAsync(host, clinic, Permissions.Emergencies.Operate, Permissions.MedicationAdministrations.Write);
            await AssertProblemAsync(await withoutRead.Client.GetAsync(Catalog), HttpStatusCode.Forbidden);
            var notStaff = await host.SignInAsync(SecurityRoles.Paramedic, [clinic], asClinicalStaff: false);
            await AssertProblemAsync(await notStaff.Client.GetAsync(Catalog), HttpStatusCode.Forbidden);
        }

        // The first clinical record of the emergency: the dose without prescription, its audit entry, Created -> InProgress
        // and the receipt.
        [SkippableFact]
        public async Task ADoseIsRecordedWithoutPrescriptionInOneTransactionAsTheFirstRecord()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var medication = await host.Web.SeedMedicationAsync();
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var administeredAt = NowToTheMillisecond().AddMinutes(-1);
            var key = Guid.NewGuid();

            var response = await PostJsonAsync(paramedic.Client, opened.Id,
                DoseJson(key, medication.Id, Utc(administeredAt), "500.0", " mg ", "\"Intravenous\"", " Lenta "));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Null(response.Headers.Location);
            var summary = await response.ReadAsAsync<EmergencyMedicationAdministrationSummary>();
            Assert.Equal((opened.Id, medication.Id, "Paracetamol", 500m, "mg", MedicationRoute.Intravenous, administeredAt, "Lenta"),
                (summary.EncounterId, summary.MedicationId, summary.MedicationName, summary.Dose, summary.DoseUnit, summary.Route, summary.AdministeredAtUtc, summary.Notes));

            var row = await host.Web.DbAsync(db => db.MedicationAdministrations.AsNoTracking().SingleAsync(a => a.ClinicalEncounterId == opened.Id));
            Assert.Equal((summary.Id, medication.Id, 500m, "mg", MedicationRoute.Intravenous, administeredAt, paramedic.StaffId!.Value),
                (row.Id, row.MedicationId, row.Dose, row.DoseUnit, row.Route, row.AdministeredAtUtc, row.AdministeredByStaffId));
            Assert.Equal(((Guid?)null, (Guid?)null, MedicationAdministrationStatus.Completed, "Lenta"),
                (row.PrescriptionId, row.PrescriptionDetailId, row.Status, row.Notes));
            Assert.Equal(0, await host.Web.DbAsync(db => db.Prescriptions.CountAsync()));

            Assert.Equal(ClinicalEncounterStatus.InProgress, await StatusAsync(host, opened.Id));
            var audit = await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .SingleAsync(a => a.ClinicalEncounterId == opened.Id && a.EntityName == nameof(MedicationAdministration)));
            Assert.Equal((AuditActionType.Create, row.Id.ToString(), paramedic.StaffId, (Guid?)null),
                (audit.Action, audit.EntityId, audit.ActorStaffId, audit.PatientId));
            var receipt = await host.Web.DbAsync(db => db.IdempotencyReceipts.AsNoTracking().SingleAsync(r => r.ClientRequestId == key));
            Assert.Equal((PrehospitalEmergencyService.RecordMedicationAdministrationOperation, row.Id), (receipt.Operation, receipt.ResourceId));

            // A later dose leaves the encounter as it is.
            var updatedAt = await host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == opened.Id).Select(e => e.UpdatedAtUtc).SingleAsync());
            Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(paramedic.Client, opened.Id, DoseJson(Guid.NewGuid(), medication.Id, Utc(NowToTheMillisecond()), notes: null))).StatusCode);
            Assert.Equal(updatedAt, await host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == opened.Id).Select(e => e.UpdatedAtUtc).SingleAsync()));
            Assert.Equal(2, await AdministrationsAsync(host, opened.Id));
        }

        [SkippableFact]
        public async Task AnIdentifiedEmergencyRecordsItsPatientInTheAudit()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var medication = await host.Web.SeedMedicationAsync();
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var nationalId = ClinicalApiHost.RandomNationalId();
            var patient = await host.Web.SeedPatientAsync(nationalId: nationalId);
            Assert.Equal(HttpStatusCode.OK, (await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{opened.Id}/identify/document",
                new { clientRequestId = Guid.NewGuid(), documentType = "NationalId", documentNumber = nationalId })).StatusCode);

            var response = await PostJsonAsync(paramedic.Client, opened.Id, DoseJson(Guid.NewGuid(), medication.Id, Utc(NowToTheMillisecond())));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.ClinicalEncounterId == opened.Id && a.EntityName == nameof(MedicationAdministration) && a.PatientId == patient.Id)));
        }

        // The wire contract: every rejection is a 400 ProblemDetails (with the field when the server owns the rule) that
        // writes nothing. The database never rounds or overflows a dose.
        [SkippableFact]
        public async Task InvalidDosesAreRejectedWithoutWritingAnything()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var medication = await host.Web.SeedMedicationAsync();
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var now = Utc(NowToTheMillisecond().AddMinutes(-1));
            string Valid() => DoseJson(Guid.NewGuid(), medication.Id, now);
            string With(string extra) => Valid().TrimEnd('}') + "," + extra + "}";

            foreach (var (json, field) in new[]
            {
                (DoseJson(Guid.NewGuid(), medication.Id, now, dose: "0"), "dose"),
                (DoseJson(Guid.NewGuid(), medication.Id, now, dose: "-1"), "dose"),
                (DoseJson(Guid.NewGuid(), medication.Id, now, dose: "1.2345"), "dose"),
                (DoseJson(Guid.NewGuid(), medication.Id, now, dose: "1000000000"), "dose"),
                (DoseJson(Guid.NewGuid(), medication.Id, now, dose: "12345678901"), "dose"),
                (DoseJson(Guid.NewGuid(), medication.Id, now, dose: "\"abc\""), null),                       // not a number
                (DoseJson(Guid.NewGuid(), medication.Id, now, doseUnit: ""), "doseUnit"),
                (DoseJson(Guid.NewGuid(), medication.Id, now, doseUnit: "   "), "doseUnit"),
                (DoseJson(Guid.NewGuid(), medication.Id, now, doseUnit: new string('u', 25)), "doseUnit"),
                (DoseJson(Guid.NewGuid(), medication.Id, now, route: "\"Intranasal\""), null),
                (DoseJson(Guid.NewGuid(), medication.Id, now, route: "\"Inhalatoria\""), null),
                (DoseJson(Guid.NewGuid(), medication.Id, now, route: "2"), null),                            // numeric
                (DoseJson(Guid.NewGuid(), medication.Id, $"{NowToTheMillisecond().UtcDateTime:yyyy-MM-dd'T'HH:mm:ss}"), null),   // no offset
                (DoseJson(Guid.NewGuid(), medication.Id, Utc(NowToTheMillisecond().AddMinutes(-11))), "administeredAt"),        // before start
                (DoseJson(Guid.NewGuid(), medication.Id, Utc(NowToTheMillisecond().AddMinutes(10))), "administeredAt"),         // beyond +5 min
                (DoseJson(Guid.NewGuid(), medication.Id, now, notes: new string('n', 4001)), "notes"),
                (DoseJson(Guid.NewGuid(), Guid.Empty, now), "medicationId"),
                (DoseJson(Guid.Empty, medication.Id, now), "clientRequestId"),
                (With($"\"prescriptionId\":\"{Guid.NewGuid()}\""), null),
                (With($"\"prescriptionDetailId\":\"{Guid.NewGuid()}\""), null),
                (With("\"medicationName\":\"Otro\""), null),
                (With("\"name\":\"Otro\""), null),
                (With("\"presentation\":\"x\""), null),
                (With("\"concentration\":\"x\""), null),
                (With("\"defaultRoute\":\"Oral\""), null),
                (With($"\"staffId\":\"{Guid.NewGuid()}\""), null),
                (With($"\"patientId\":\"{Guid.NewGuid()}\""), null),
                (With($"\"clinicId\":\"{Guid.NewGuid()}\""), null),
                (With("\"status\":\"Completed\""), null),
                (With($"\"administeredAtUtc\":\"{now}\""), null)
            })
            {
                var response = await PostJsonAsync(paramedic.Client, opened.Id, json);
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {json}");
                var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
                if (field is not null)
                {
                    Assert.True(problem.TryGetProperty("field", out var actual) && actual.GetString() == field, $"{problem}: {json}");
                }
            }

            Assert.Equal(0, await AdministrationsAsync(host, opened.Id));
            Assert.Equal(0, await AuditsAsync(host, opened.Id));
            Assert.Equal(0, await ReceiptsAsync(host));
            Assert.Equal(ClinicalEncounterStatus.Created, await StatusAsync(host, opened.Id));

            // The limits themselves are stored exactly, never rounded.
            Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(paramedic.Client, opened.Id, DoseJson(Guid.NewGuid(), medication.Id, now, dose: "999999999.999"))).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(paramedic.Client, opened.Id, DoseJson(Guid.NewGuid(), medication.Id, now, dose: "0.125", doseUnit: new string('u', 24)))).StatusCode);
            Assert.Equal([0.125m, 999999999.999m], (await host.Web.DbAsync(db => db.MedicationAdministrations.Where(a => a.ClinicalEncounterId == opened.Id)
                .Select(a => a.Dose).ToListAsync())).Order());
        }

        [SkippableFact]
        public async Task AnUnknownMedicationIsNotFoundAndAnInactiveOneIsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var inactive = await host.Web.SeedMedicationAsync("Retirado", active: false);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));

            await AssertProblemAsync(await PostJsonAsync(paramedic.Client, opened.Id, DoseJson(Guid.NewGuid(), Guid.NewGuid(), Utc(NowToTheMillisecond()))), HttpStatusCode.NotFound);
            await AssertProblemAsync(await PostJsonAsync(paramedic.Client, opened.Id, DoseJson(Guid.NewGuid(), inactive.Id, Utc(NowToTheMillisecond()))), HttpStatusCode.Conflict);

            Assert.Equal(0, await AdministrationsAsync(host, opened.Id));
            Assert.Equal(0, await ReceiptsAsync(host));
            Assert.Equal(ClinicalEncounterStatus.Created, await StatusAsync(host, opened.Id));
        }

        // Same key + same dose (500 = 500.0, Z or offset, unit and notes with other spaces): 200 with the same
        // administration. Same key with any other value or emergency: 409 and the original stays as it was. A replay is
        // still answered after the medication was deactivated.
        [SkippableFact]
        public async Task RetryingReplaysAndAReusedKeyWithOtherDataIsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var medication = await host.Web.SeedMedicationAsync();
            var other = await host.Web.SeedMedicationAsync("Metamizol");
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var otherEmergency = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var administeredAt = NowToTheMillisecond().AddMinutes(-1);
            var key = Guid.NewGuid();

            var first = await PostJsonAsync(paramedic.Client, opened.Id, DoseJson(key, medication.Id, Utc(administeredAt)));
            var replay = await PostJsonAsync(paramedic.Client, opened.Id, DoseJson(key, medication.Id,
                administeredAt.ToOffset(TimeSpan.FromHours(-4)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", Inv), "500.000", " mg", notes: "Con agua "));

            Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.StatusCode, replay.StatusCode));
            var created = await first.ReadAsAsync<EmergencyMedicationAdministrationSummary>();
            Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(await replay.ReadAsAsync<EmergencyMedicationAdministrationSummary>()));

            foreach (var (emergencyId, json) in new[]
            {
                (opened.Id, DoseJson(key, other.Id, Utc(administeredAt))),
                (opened.Id, DoseJson(key, medication.Id, Utc(administeredAt), dose: "500.001")),
                (opened.Id, DoseJson(key, medication.Id, Utc(administeredAt), doseUnit: "mcg")),
                (opened.Id, DoseJson(key, medication.Id, Utc(administeredAt), route: "\"Intravenous\"")),
                (opened.Id, DoseJson(key, medication.Id, Utc(administeredAt.AddSeconds(-1)))),
                (opened.Id, DoseJson(key, medication.Id, Utc(administeredAt), notes: null)),
                (otherEmergency.Id, DoseJson(key, medication.Id, Utc(administeredAt)))
            })
            {
                await AssertProblemAsync(await PostJsonAsync(paramedic.Client, emergencyId, json), HttpStatusCode.Conflict);
            }

            var stored = await host.Web.DbAsync(db => db.MedicationAdministrations.AsNoTracking().SingleAsync(a => a.ClinicalEncounterId == opened.Id));
            Assert.Equal((created.Id, medication.Id, 500m, "mg", MedicationRoute.Oral, "Con agua"),
                (stored.Id, stored.MedicationId, stored.Dose, stored.DoseUnit, stored.Route, stored.Notes));
            Assert.Equal(1, await AuditsAsync(host, opened.Id));
            Assert.Equal(0, await AdministrationsAsync(host, otherEmergency.Id));
            Assert.Equal(1, await ReceiptsAsync(host));

            await host.Web.DbAsync(async db =>
            {
                (await db.Medications.SingleAsync(m => m.Id == medication.Id)).Deactivate();
                await db.SaveChangesAsync();
            });
            var afterDeactivation = await PostJsonAsync(paramedic.Client, opened.Id, DoseJson(key, medication.Id, Utc(administeredAt)));
            Assert.Equal(HttpStatusCode.OK, afterDeactivation.StatusCode);
            Assert.Equal(created.Id, (await afterDeactivation.ReadAsAsync<EmergencyMedicationAdministrationSummary>()).Id);
            await AssertProblemAsync(await PostJsonAsync(paramedic.Client, opened.Id, DoseJson(Guid.NewGuid(), medication.Id, Utc(administeredAt))), HttpStatusCode.Conflict);
        }

        [SkippableFact]
        public async Task AConcurrentDoubleSubmitRecordsTheDoseOnce()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var medication = await host.Web.SeedMedicationAsync();

            for (var round = 0; round < 5; round++)
            {
                var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
                var json = DoseJson(Guid.NewGuid(), medication.Id, Utc(NowToTheMillisecond().AddMinutes(-1)));
                using var start = new ManualResetEventSlim();
                Task<HttpResponseMessage> Submit() => Task.Run(async () => { start.Wait(); return await PostJsonAsync(paramedic.Client, opened.Id, json); });
                var tasks = new[] { Submit(), Submit() };
                start.Set();
                var responses = await Task.WhenAll(tasks);

                Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created], responses.Select(r => r.StatusCode).Order());
                var ids = await Task.WhenAll(responses.Select(r => r.ReadAsAsync<EmergencyMedicationAdministrationSummary>()));
                Assert.Equal(ids[0].Id, ids[1].Id);
                Assert.Equal(1, await AdministrationsAsync(host, opened.Id));
                Assert.Equal(1, await AuditsAsync(host, opened.Id));
            }

            Assert.Equal(5, await ReceiptsAsync(host));
        }

        // Deterministic race of the same dose: another transaction holds its receipt and the start of the emergency,
        // uncommitted. The request waits on them and, when they commit, answers 200 from the receipt: no duplicate, no 500.
        [SkippableFact]
        public async Task LosingTheRaceToTheSameDoseReturnsTheWinner()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var medication = await host.Web.SeedMedicationAsync();
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var administeredAt = NowToTheMillisecond().AddMinutes(-1);
            var key = Guid.NewGuid();

            await using var other = PostgresFixture.Clinical(host.ConnectionString);
            await using var transaction = await other.Database.BeginTransactionAsync();
            var encounter = await other.ClinicalEncounters.SingleAsync(e => e.Id == opened.Id);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, DateTimeOffset.UtcNow);
            var winner = MedicationAdministration.Record(opened.Id, medication.Id, 500m, "mg", MedicationRoute.Oral, administeredAt, paramedic.StaffId!.Value, notes: "Con agua");
            other.MedicationAdministrations.Add(winner);
            other.IdempotencyReceipts.Add(IdempotencyReceiptEntry.From(new IdempotencyReceipt(
                new IdempotencyKey(paramedic.UserId, PrehospitalEmergencyService.RecordMedicationAdministrationOperation, key),
                IdempotencyRequestHash.Compute(opened.Id.ToString("D"), medication.Id.ToString("D"), "500", "mg", "Oral",
                    IdempotencyRequestHash.Instant(administeredAt), "Con agua"),
                winner.Id, DateTimeOffset.UtcNow)));
            await other.SaveChangesAsync();

            var request = PostJsonAsync(paramedic.Client, opened.Id, DoseJson(key, medication.Id, Utc(administeredAt)));
            await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
            await transaction.CommitAsync();
            var response = await request;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(winner.Id, (await response.ReadAsAsync<EmergencyMedicationAdministrationSummary>()).Id);
            Assert.Equal(1, await AdministrationsAsync(host, opened.Id));
            Assert.Equal(0, await AuditsAsync(host, opened.Id));
            Assert.Equal(1, await ReceiptsAsync(host));
        }

        // EnsureEncounterUnchanged: another transaction closes, dispatches (InProgress -> Transferred) or identifies the
        // emergency while the dose is being recorded. The dose loses with a 409 and nothing of it is written.
        [SkippableTheory]
        [InlineData("close")]
        [InlineData("dispatch")]
        [InlineData("identify")]
        public async Task ADoseLosesTheRaceAgainstAConcurrentChangeOfTheEmergency(string change)
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var medication = await host.Web.SeedMedicationAsync();
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            await StartAsync(paramedic, opened.Id);
            var patient = await host.Web.SeedPatientAsync(nationalId: ClinicalApiHost.RandomNationalId());

            var (connection, competing) = change switch
            {
                "close" => await ClinicalRace.BeginCloseAsync(host.ConnectionString, opened.Id),
                "dispatch" => await ClinicalRace.BeginStatusChangeAsync(host.ConnectionString, opened.Id, from: 2, to: 3),
                _ => await BeginIdentifyAsync(host.ConnectionString, opened.Id, patient.Id, paramedic.StaffId!.Value)
            };

            await using (connection)
            {
                var request = PostJsonAsync(paramedic.Client, opened.Id, DoseJson(Guid.NewGuid(), medication.Id, Utc(NowToTheMillisecond())));
                await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
                await competing.CommitAsync();

                await AssertProblemAsync(await request, HttpStatusCode.Conflict);
            }

            Assert.Equal(0, await AdministrationsAsync(host, opened.Id));
            Assert.Equal(0, await AuditsAsync(host, opened.Id));
            Assert.Equal(0, await ReceiptsAsync(host));
        }

        [SkippableFact]
        public async Task AClosedOrCancelledEmergencyRejectsTheDose()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var medication = await host.Web.SeedMedicationAsync();

            // Transferred without clinic is the way to the hospital and accepts records (MobileTransferApiTests).
            foreach (var target in new[] { ClinicalEncounterStatus.Closed, ClinicalEncounterStatus.Cancelled })
            {
                var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
                await host.Web.DbAsync(async db =>
                {
                    var encounter = await db.ClinicalEncounters.SingleAsync(e => e.Id == opened.Id);
                    if (target != ClinicalEncounterStatus.Cancelled)
                    {
                        encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, DateTimeOffset.UtcNow);
                    }

                    encounter.ChangeStatus(target, DateTimeOffset.UtcNow);
                    await db.SaveChangesAsync();
                });

                await AssertProblemAsync(await PostJsonAsync(paramedic.Client, opened.Id, DoseJson(Guid.NewGuid(), medication.Id, Utc(NowToTheMillisecond()))), HttpStatusCode.Conflict);
                Assert.Equal(0, await AdministrationsAsync(host, opened.Id));
                Assert.Equal(target, await StatusAsync(host, opened.Id));
            }

            Assert.Equal(0, await ReceiptsAsync(host));
        }

        // Permissions are the authority: no token 401; without Emergencies.Operate (doctor, nurse: they have
        // MedicationAdministrations.Write) or without MedicationAdministrations.Write (a test role) 403; another paramedic
        // 403; unknown or non-prehospital encounter 404.
        [SkippableFact]
        public async Task OnlyTheAssignedParamedicWithBothPermissionsRecordsDoses()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var clinic = await host.Web.CreateClinicAsync();
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var otherParamedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var medication = await host.Web.SeedMedicationAsync();
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            string Body() => DoseJson(Guid.NewGuid(), medication.Id, Utc(NowToTheMillisecond()));

            Assert.Equal(HttpStatusCode.Unauthorized, (await PostJsonAsync(host.Anonymous(), opened.Id, Body())).StatusCode);
            foreach (var (role, type) in new[]
            {
                (SecurityRoles.Doctor, ClinicalStaffType.Physician),
                (SecurityRoles.Nurse, ClinicalStaffType.Nurse),
                (SecurityRoles.Receptionist, ClinicalStaffType.OtherClinical)
            })
            {
                var user = await host.SignInAsync(role, [clinic], staffType: type);
                await AssertProblemAsync(await PostJsonAsync(user.Client, opened.Id, Body()), HttpStatusCode.Forbidden);
            }

            var withoutWrite = await WithPermissionsAsync(host, clinic,
                Permissions.Emergencies.Operate, Permissions.Encounters.Read, Permissions.Medications.Read);
            await AssertProblemAsync(await PostJsonAsync(withoutWrite.Client, opened.Id, Body()), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await PostJsonAsync(otherParamedic.Client, opened.Id, Body()), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await PostJsonAsync(paramedic.Client, Guid.NewGuid(), Body()), HttpStatusCode.NotFound);

            var hospital = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, DateTimeOffset.UtcNow.AddMinutes(-30), clinic.Id);
            await host.Web.DbAsync(async db => { db.ClinicalEncounters.Add(hospital); await db.SaveChangesAsync(); });
            await AssertProblemAsync(await PostJsonAsync(paramedic.Client, hospital.Id, Body()), HttpStatusCode.NotFound);

            Assert.Equal(0, await AdministrationsAsync(host, opened.Id));
            Assert.Equal(0, await ReceiptsAsync(host));
            Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(paramedic.Client, opened.Id, Body())).StatusCode);
        }

        // Regression: the Web path keeps the clinic rule and its own contract (a clinic encounter, its clinic's nurse).
        [SkippableFact]
        public async Task TheWebAdministrationKeepsTheClinicRule()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinicA = await host.CreateClinicAsync("Hospital A");
            var clinicB = await host.CreateClinicAsync("Hospital B");
            var nurseA = await host.SignInAsync(SecurityRoles.Nurse, [clinicA], staffType: ClinicalStaffType.Nurse);
            var nurseB = await host.SignInAsync(SecurityRoles.Nurse, [clinicB], staffType: ClinicalStaffType.Nurse);
            var medication = await host.SeedMedicationAsync();
            var encounter = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, DateTimeOffset.UtcNow.AddMinutes(-30), clinicA.Id);
            await host.DbAsync(async db => { db.ClinicalEncounters.Add(encounter); await db.SaveChangesAsync(); });
            object Body() => new { medicationId = medication.Id, dose = 1, doseUnit = "g", route = "Oral", administeredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };

            Assert.Equal(HttpStatusCode.Created, (await nurseA.Client.PostAsJsonAsync($"/api/web/encounters/{encounter.Id}/medication-administrations", Body())).StatusCode);
            await AssertProblemAsync(await nurseB.Client.PostAsJsonAsync($"/api/web/encounters/{encounter.Id}/medication-administrations", Body()), HttpStatusCode.Forbidden);
        }

        private static async Task<(NpgsqlConnection, NpgsqlTransaction)> BeginIdentifyAsync(string connectionString, Guid encounterId, Guid patientId, Guid staffId)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            await using var update = new NpgsqlCommand(
                """UPDATE clinical."ClinicalEncounters" SET "PatientId" = @patient, "IdentifiedByStaffId" = @staff, "IdentifiedAtUtc" = GREATEST(now(), "StartedAtUtc") WHERE "Id" = @id AND "PatientId" IS NULL""",
                connection, transaction);
            update.Parameters.AddWithValue("id", encounterId);
            update.Parameters.AddWithValue("patient", patientId);
            update.Parameters.AddWithValue("staff", staffId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
            return (connection, transaction);
        }

        private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
            return problem.RootElement.Clone();
        }
    }
}
