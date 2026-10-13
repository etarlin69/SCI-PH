using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Medication catalog, prescriptions and medication administrations (the Kardex) through the real Web BFF over
    // PostgreSQL, including the race between closing an encounter and a medication write.
    [Collection(PostgresCollection.Name)]
    public sealed class MedicationsKardexApiTests
    {
        private const string Encounters = "/api/web/encounters";
        private const string Medications = "/api/web/medications";
        private readonly PostgresFixture _postgres;

        public MedicationsKardexApiTests(PostgresFixture postgres) => _postgres = postgres;

        private sealed record Setup(
            ClinicalApiHost Host, StaffSession Doctor, StaffSession Nurse, StaffSession Reception, EncounterSummary Encounter,
            Medication Paracetamol, Medication Ibuprofen);

        // An encounter already in progress (first vital sign by the nurse), two active medications, a doctor, a nurse
        // and a receptionist of the same clinic.
        private async Task<Setup> InProgressAsync()
        {
            var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            var nurse = await host.SignInAsync(SecurityRoles.Nurse, [clinic], staffType: ClinicalStaffType.Nurse);
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var encounter = await OpenEncounterAsync(host, reception, nurse);
            return new Setup(host, doctor, nurse, reception, encounter,
                await host.SeedMedicationAsync("Paracetamol"), await host.SeedMedicationAsync("Ibuprofeno", defaultRoute: null));
        }

        private static async Task<EncounterSummary> OpenEncounterAsync(ClinicalApiHost host, StaffSession reception, StaffSession nurse)
        {
            var created = await reception.Client.PostAsJsonAsync(Encounters, new { type = "Emergency", patientId = (await host.SeedPatientAsync()).Id });
            var encounter = await created.ReadAsAsync<EncounterSummary>();
            var vital = await nurse.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/vital-signs",
                new { type = "HeartRate", value = 90, measuredAtUtc = DateTimeOffset.UtcNow });
            Assert.Equal(HttpStatusCode.Created, vital.StatusCode);
            return encounter;
        }

        private static object Line(Guid medicationId, decimal dose = 500, string route = "Oral") =>
            new { medicationId, dose, doseUnit = "mg", route, frequency = "cada 8 horas", durationDays = 5, instructions = "Con alimentos" };

        private static object Order(params object[] details) => new { notes = "Tratamiento (dato de prueba).", details };

        private static object Dose(Guid medicationId, Guid? prescriptionId = null, Guid? prescriptionDetailId = null) =>
            new { medicationId, dose = 500, doseUnit = "mg", route = "Oral", administeredAtUtc = DateTimeOffset.UtcNow, prescriptionId, prescriptionDetailId };

        [SkippableFact]
        public async Task CatalogPrescriptionsAndKardexArePersistedAndListed()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;

            // Catalog: enums as text, a missing default route as null.
            using (var catalog = JsonDocument.Parse(await s.Nurse.Client.GetStringAsync(Medications)))
            {
                var paracetamol = catalog.RootElement.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == s.Paracetamol.Id);
                var ibuprofen = catalog.RootElement.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == s.Ibuprofen.Id);
                Assert.Equal("Oral", paracetamol.GetProperty("defaultRoute").GetString());
                Assert.Equal("Active", paracetamol.GetProperty("status").GetString());
                Assert.Equal(JsonValueKind.Null, ibuprofen.GetProperty("defaultRoute").ValueKind);
            }

            var created = await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/prescriptions",
                Order(Line(s.Paracetamol.Id), Line(s.Ibuprofen.Id, 400)));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.EndsWith($"{Encounters}/{id}/prescriptions", created.Headers.Location!.ToString());
            var prescription = await created.ReadAsAsync<PrescriptionSummary>();
            Assert.Equal(PrescriptionStatus.Issued, prescription.Status);
            Assert.Equal(s.Doctor.StaffId, prescription.PrescribedByStaffId);
            var paracetamolLine = prescription.Details.Single(d => d.MedicationId == s.Paracetamol.Id);

            // Two doses of the prescribed line and one dose outside any prescription.
            var first = await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/medication-administrations",
                Dose(s.Paracetamol.Id, prescription.Id, paracetamolLine.Id));
            var second = await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/medication-administrations",
                Dose(s.Paracetamol.Id, prescription.Id, paracetamolLine.Id));
            var free = await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/medication-administrations", Dose(s.Ibuprofen.Id));
            Assert.All(new[] { first, second, free }, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            Assert.EndsWith($"{Encounters}/{id}/medication-administrations", first.Headers.Location!.ToString());

            // A fresh read (as after F5) returns what PostgreSQL stored.
            var prescriptions = await (await s.Nurse.Client.GetAsync($"{Encounters}/{id}/prescriptions")).ReadAsAsync<List<PrescriptionSummary>>();
            var kardex = await (await s.Doctor.Client.GetAsync($"{Encounters}/{id}/medication-administrations"))
                .ReadAsAsync<List<MedicationAdministrationSummary>>();
            var listed = Assert.Single(prescriptions);
            Assert.Equal(["Ibuprofeno", "Paracetamol"], listed.Details.Select(d => d.MedicationName));
            Assert.Equal(400, listed.Details[0].Dose);
            Assert.Equal(3, kardex.Count);
            Assert.Equal(2, kardex.Count(a => a.PrescriptionDetailId == paracetamolLine.Id && a.AdministeredByStaffId == s.Nurse.StaffId));
            Assert.Contains(kardex, a => a.PrescriptionId is null && a.MedicationName == "Ibuprofeno" && a.AdministeredByStaffId == s.Doctor.StaffId);
            Assert.All(kardex, a => Assert.Equal(MedicationAdministrationStatus.Completed, a.Status));
            using (var raw = JsonDocument.Parse(await s.Doctor.Client.GetStringAsync($"{Encounters}/{id}/medication-administrations")))
            {
                Assert.Equal("Oral", raw.RootElement[0].GetProperty("route").GetString());
                Assert.Equal("Completed", raw.RootElement[0].GetProperty("status").GetString());
            }

            // Relationships in the database: details belong to the prescription, doses to the encounter and the line.
            Assert.Equal(2, await s.Host.DbAsync(db => db.PrescriptionDetails.CountAsync(d => d.PrescriptionId == prescription.Id)));
            Assert.Equal(2, await s.Host.DbAsync(db => db.MedicationAdministrations.CountAsync(
                a => a.ClinicalEncounterId == id && a.PrescriptionId == prescription.Id && a.PrescriptionDetailId == paracetamolLine.Id)));
            Assert.Equal(4, await s.Host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.ClinicalEncounterId == id && (a.EntityName == "Prescription" || a.EntityName == "MedicationAdministration"))));
        }

        [SkippableFact]
        public async Task CloseBlocksNewPrescriptionsAndAdministrationsButKeepsTheKardexReadable()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var prescription = await (await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/prescriptions", Order(Line(s.Paracetamol.Id))))
                .ReadAsAsync<PrescriptionSummary>();
            Assert.Equal(HttpStatusCode.Created, (await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/medication-administrations",
                Dose(s.Paracetamol.Id, prescription.Id, prescription.Details[0].Id))).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/close", new { })).StatusCode);

            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/prescriptions", Order(Line(s.Paracetamol.Id))),
                HttpStatusCode.Conflict);
            await AssertStatusAsync(await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/medication-administrations",
                Dose(s.Paracetamol.Id, prescription.Id, prescription.Details[0].Id)), HttpStatusCode.Conflict);
            Assert.Single(await (await s.Nurse.Client.GetAsync($"{Encounters}/{id}/prescriptions")).ReadAsAsync<List<PrescriptionSummary>>());
            Assert.Single(await (await s.Nurse.Client.GetAsync($"{Encounters}/{id}/medication-administrations"))
                .ReadAsAsync<List<MedicationAdministrationSummary>>());
        }

        [SkippableFact]
        public async Task PermissionsFollowTheRealRoleMatrix()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var anonymous = s.Host.Anonymous();

            foreach (var response in new[]
            {
                await anonymous.GetAsync(Medications),
                await anonymous.GetAsync($"{Encounters}/{id}/prescriptions"),
                await anonymous.PostAsJsonAsync($"{Encounters}/{id}/prescriptions", Order(Line(s.Paracetamol.Id))),
                await anonymous.GetAsync($"{Encounters}/{id}/medication-administrations"),
                await anonymous.PostAsJsonAsync($"{Encounters}/{id}/medication-administrations", Dose(s.Paracetamol.Id))
            })
            {
                await AssertStatusAsync(response, HttpStatusCode.Unauthorized);
            }

            // Nurse: reads everything and administers, but has no Prescriptions.Write.
            await AssertStatusAsync(await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/prescriptions", Order(Line(s.Paracetamol.Id))),
                HttpStatusCode.Forbidden);
            Assert.Equal(HttpStatusCode.Created,
                (await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/medication-administrations", Dose(s.Paracetamol.Id))).StatusCode);
            // Receptionist: no clinical access at all (no Medications.Read, Encounters.Read, Prescriptions.Write nor MedicationAdministrations.Write).
            await AssertStatusAsync(await s.Reception.Client.GetAsync(Medications), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await s.Reception.Client.GetAsync($"{Encounters}/{id}/prescriptions"), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await s.Reception.Client.GetAsync($"{Encounters}/{id}/medication-administrations"), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await s.Reception.Client.PostAsJsonAsync($"{Encounters}/{id}/prescriptions", Order(Line(s.Paracetamol.Id))),
                HttpStatusCode.Forbidden);
            await AssertStatusAsync(await s.Reception.Client.PostAsJsonAsync($"{Encounters}/{id}/medication-administrations", Dose(s.Paracetamol.Id)),
                HttpStatusCode.Forbidden);
            // Permission without being clinical staff is not enough.
            var unlinkedDoctor = await s.Host.SignInAsync(SecurityRoles.Doctor, [], asClinicalStaff: false);
            await AssertStatusAsync(await unlinkedDoctor.Client.GetAsync(Medications), HttpStatusCode.Forbidden);

            Assert.Equal(0, await s.Host.DbAsync(db => db.Prescriptions.CountAsync(p => p.ClinicalEncounterId == id)));
            Assert.Equal(1, await s.Host.DbAsync(db => db.MedicationAdministrations.CountAsync(a => a.ClinicalEncounterId == id)));
        }

        [SkippableFact]
        public async Task NotFoundValidationConflictsAndClinicIsolation()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var retired = await s.Host.SeedMedicationAsync("Retirado", active: false);
            var otherEncounter = await OpenEncounterAsync(s.Host, s.Reception, s.Nurse);
            var foreignPrescription = await (await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{otherEncounter.Id}/prescriptions",
                Order(Line(s.Paracetamol.Id)))).ReadAsAsync<PrescriptionSummary>();
            var prescriptions = $"{Encounters}/{id}/prescriptions";
            var administrations = $"{Encounters}/{id}/medication-administrations";

            // 404: unknown encounter, medication, prescription, or a prescription of another encounter.
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{Guid.NewGuid()}/prescriptions", Order(Line(s.Paracetamol.Id))),
                HttpStatusCode.NotFound);
            await AssertStatusAsync(await s.Doctor.Client.GetAsync($"{Encounters}/{Guid.NewGuid()}/medication-administrations"), HttpStatusCode.NotFound);
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync(prescriptions, Order(Line(Guid.NewGuid()))), HttpStatusCode.NotFound);
            await AssertStatusAsync(await s.Nurse.Client.PostAsJsonAsync(administrations, Dose(s.Paracetamol.Id, Guid.NewGuid())), HttpStatusCode.NotFound);
            await AssertStatusAsync(await s.Nurse.Client.PostAsJsonAsync(administrations,
                Dose(s.Paracetamol.Id, foreignPrescription.Id, foreignPrescription.Details[0].Id)), HttpStatusCode.NotFound);

            // 409: a medication deactivated in the catalog.
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync(prescriptions, Order(Line(retired.Id))), HttpStatusCode.Conflict);
            await AssertStatusAsync(await s.Nurse.Client.PostAsJsonAsync(administrations, Dose(retired.Id)), HttpStatusCode.Conflict);

            // 400: empty order, invalid values, numeric enums, server-owned fields sent by the client.
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync(prescriptions, Order()), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync(prescriptions, Order(Line(s.Paracetamol.Id, dose: 0))), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync(prescriptions,
                new { details = new[] { new { medicationId = s.Paracetamol.Id, dose = 1, doseUnit = "mg", route = 1, frequency = "c/8h" } } }),
                HttpStatusCode.BadRequest);
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync(prescriptions,
                new { prescribedByStaffId = Guid.NewGuid(), details = new[] { Line(s.Paracetamol.Id) } }), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await s.Nurse.Client.PostAsJsonAsync(administrations,
                new { medicationId = s.Paracetamol.Id, dose = 1, doseUnit = "mg", route = "Oral", administeredAtUtc = DateTimeOffset.UtcNow,
                    administeredByStaffId = Guid.NewGuid() }), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await s.Nurse.Client.PostAsJsonAsync(administrations,
                new { medicationId = s.Paracetamol.Id, dose = 1, doseUnit = "mg", route = "Oral", administeredAtUtc = DateTimeOffset.UtcNow.AddHours(1) }),
                HttpStatusCode.BadRequest);

            // 403: another clinic's staff cannot read or write this encounter's treatment.
            var otherClinic = await s.Host.CreateClinicAsync("Hospital ajeno");
            var foreignDoctor = await s.Host.SignInAsync(SecurityRoles.Doctor, [otherClinic], staffType: ClinicalStaffType.Physician);
            await AssertStatusAsync(await foreignDoctor.Client.GetAsync(prescriptions), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await foreignDoctor.Client.GetAsync(administrations), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await foreignDoctor.Client.PostAsJsonAsync(prescriptions, Order(Line(s.Paracetamol.Id))), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await foreignDoctor.Client.PostAsJsonAsync(administrations, Dose(s.Paracetamol.Id)), HttpStatusCode.Forbidden);

            Assert.Equal(0, await s.Host.DbAsync(db => db.Prescriptions.CountAsync(p => p.ClinicalEncounterId == id)));
            Assert.Equal(0, await s.Host.DbAsync(db => db.MedicationAdministrations.CountAsync(a => a.ClinicalEncounterId == id)));
        }

        // Another transaction closes the encounter and stays open while the medication write is processed: the request
        // passes every check, blocks on the encounter row and, once the close commits, fails with 409 leaving nothing.
        [SkippableTheory]
        [InlineData("prescriptions")]
        [InlineData("medication-administrations")]
        public async Task AConcurrentCloseWinsOverAMedicationWrite(string record)
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;

            var (connection, close) = await ClinicalRace.BeginCloseAsync(s.Host.ConnectionString, id);
            await using var __ = connection;
            await using var ___ = close;
            var write = record == "prescriptions"
                ? s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/prescriptions", Order(Line(s.Paracetamol.Id)))
                : s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/medication-administrations", Dose(s.Paracetamol.Id));

            await ClinicalRace.WaitUntilBlockedOnLockAsync(s.Host.ConnectionString, write);
            await close.CommitAsync();

            await AssertStatusAsync(await write, HttpStatusCode.Conflict);
            Assert.Equal(0, await CountMedicationRecordsAsync(s.Host, id));
            Assert.Equal(ClinicalEncounterStatus.Closed,
                await s.Host.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == id).Select(e => e.Status).SingleAsync()));
        }

        // Real parallel requests, several rounds: the close always wins its own request, the writes answer 201 or 409
        // (never 500), and every accepted write was committed before the close.
        [SkippableFact]
        public async Task ParallelCloseAndMedicationWritesNeverFailUnexpectedly()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;

            for (var round = 0; round < 4; round++)
            {
                var encounter = await OpenEncounterAsync(s.Host, s.Reception, s.Nurse);
                using var start = new ManualResetEventSlim();
                Task<HttpResponseMessage> Run(Func<Task<HttpResponseMessage>> call) => Task.Run(async () => { start.Wait(); return await call(); });
                var tasks = new[]
                {
                    Run(() => s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/close", new { })),
                    Run(() => s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/prescriptions", Order(Line(s.Paracetamol.Id)))),
                    Run(() => s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/medication-administrations", Dose(s.Ibuprofen.Id)))
                };
                start.Set();
                var responses = await Task.WhenAll(tasks);

                Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
                Assert.All(responses.Skip(1), r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
                Assert.Equal(ClinicalEncounterStatus.Closed,
                    await s.Host.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == encounter.Id).Select(e => e.Status).SingleAsync()));
                Assert.Equal(responses.Skip(1).Count(r => r.StatusCode == HttpStatusCode.Created), await CountMedicationRecordsAsync(s.Host, encounter.Id));
            }
        }

        private static Task<int> CountMedicationRecordsAsync(ClinicalApiHost host, Guid encounterId) =>
            host.DbAsync(async db =>
                await db.Prescriptions.CountAsync(p => p.ClinicalEncounterId == encounterId)
                + await db.MedicationAdministrations.CountAsync(a => a.ClinicalEncounterId == encounterId));

        private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
        }
    }
}
