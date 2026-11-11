using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Idempotency of the QR confirmation (issue #53) through the real Mobile BFF over PostgreSQL. The same-key race window
    // (between the receipt lookup and the emergency load) is made deterministic with ReceiptLookupGate: the loser holds
    // its "no receipt" answer while the other request runs to completion.
    [Collection(PostgresCollection.Name)]
    public sealed class MobileQrConfirmationRaceApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private const string Document = "001-1234567-8";
        private readonly PostgresFixture _postgres;

        public MobileQrConfirmationRaceApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static async Task<(Guid PatientId, string Payload)> PatientWithQrAsync(MobileEmergencyHost host, string givenNames)
        {
            var (record, patient) = await host.SignInPatientAsync(givenNames);
            var issued = await patient.PostAsync("/api/mobile/patient/me/qr", null);
            Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
            using var json = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
            return (record.PatientId, json.RootElement.GetProperty("payload").GetString()!);
        }

        private static async Task<StaffSession> ParamedicAsync(MobileEmergencyHost host, Domain.Entities.Organizations.Clinic? clinic = null) =>
            await host.SignInAsync(SecurityRoles.Paramedic, [clinic ?? await host.Web.CreateClinicAsync()]);

        private static async Task<PrehospitalEmergencySummary> OpenAsync(StaffSession paramedic)
        {
            var response = await paramedic.Client.PostAsJsonAsync(Emergencies, new
            {
                clientRequestId = Guid.NewGuid(),
                startedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                chiefComplaint = "Inconsciente"
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<PrehospitalEmergencySummary>();
        }

        private static Task<HttpResponseMessage> ConfirmAsync(StaffSession staff, Guid emergencyId, string payload, Guid clientRequestId) =>
            staff.Client.PostAsJsonAsync($"{Emergencies}/{emergencyId}/identify/qr/confirm", new { clientRequestId, payload });

        private static Task<List<IdentificationAttempt>> SuccessfulAttemptsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.IdentificationAttempts.AsNoTracking()
                .Where(a => a.ClinicalEncounterId == emergencyId && a.Outcome == IdentificationOutcome.Success).ToListAsync());

        private static Task<int> QrReceiptCountAsync(MobileEmergencyHost host, Guid? clientRequestId = null) =>
            host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == EmergencyQrIdentificationService.ConfirmOperation
                && (clientRequestId == null || r.ClientRequestId == clientRequestId)));

        private static Task<int> IdentifyAuditsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergencyId && a.Action == AuditActionType.Identify));

        private static Task<Domain.Entities.Encounters.ClinicalEncounter> EncounterAsync(MobileEmergencyHost host, Guid id) =>
            host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == id));

        private static async Task AssertConflictAsync(HttpResponseMessage response, string payload)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain(payload, await response.Content.ReadAsStringAsync());
        }

        // The double submit: the first confirmation looked up its receipt (none) and the SAME request (same user, key and
        // QR) committed before it loaded the emergency. It replays the winner: 200 + 200 with the same emergency, patient and
        // NN code, one identification, one successful attempt, one receipt.
        [SkippableFact]
        public async Task ASameKeyConfirmThatLosesTheRaceReplaysTheWinner()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var gate = new ReceiptLookupGate();
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, mobileServices: s => s.AddReceiptLookupGate(gate));
            var (patientId, payload) = await PatientWithQrAsync(host, "Alba");
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            var key = Guid.NewGuid();

            gate.Arm(EmergencyQrIdentificationService.ConfirmOperation, key);
            var loser = ConfirmAsync(paramedic, emergency.Id, payload, key);
            await gate.Reached;
            var winner = await ConfirmAsync(paramedic, emergency.Id, payload, key);
            Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
            gate.Release();
            var replayed = await loser;

            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            var results = new[] { await winner.ReadAsAsync<PrehospitalEmergencySummary>(), await replayed.ReadAsAsync<PrehospitalEmergencySummary>() };
            Assert.All(results, r => Assert.Equal((emergency.Id, emergency.TemporaryPatientCode, patientId), (r.Id, r.TemporaryPatientCode, r.Patient!.Id)));
            var encounter = await EncounterAsync(host, emergency.Id);
            Assert.Equal(((Guid?)patientId, emergency.TemporaryPatientCode), (encounter.PatientId, encounter.TemporaryPatientCode!.Value));
            var success = Assert.Single(await SuccessfulAttemptsAsync(host, emergency.Id));
            Assert.Equal((IdentificationMethod.QrCode, (Guid?)patientId), (success.Method, success.PatientId));
            Assert.Equal(1, await QrReceiptCountAsync(host, key));
            Assert.Equal(1, await IdentifyAuditsAsync(host, emergency.Id));
        }

        // The same key with ANOTHER QR committed in that window is not the same request: 409, never a replay; the winner's
        // patient stays.
        [SkippableFact]
        public async Task ASameKeyConfirmWithAnotherQrThatLosesTheRaceIsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var gate = new ReceiptLookupGate();
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, mobileServices: s => s.AddReceiptLookupGate(gate));
            var (_, payload) = await PatientWithQrAsync(host, "Alba");
            var (otherId, otherPayload) = await PatientWithQrAsync(host, "Bruno");
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            var key = Guid.NewGuid();

            gate.Arm(EmergencyQrIdentificationService.ConfirmOperation, key);
            var loser = ConfirmAsync(paramedic, emergency.Id, payload, key);
            await gate.Reached;
            Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(paramedic, emergency.Id, otherPayload, key)).StatusCode);
            gate.Release();

            await AssertConflictAsync(await loser, payload);
            Assert.Equal(otherId, (await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Equal(otherId, Assert.Single(await SuccessfulAttemptsAsync(host, emergency.Id)).PatientId);
            Assert.Equal(1, await QrReceiptCountAsync(host, key));
        }

        // Another METHOD (document) identifying inside that window leaves no QR receipt: the loser stays a 409, the
        // document's patient is kept and no QR receipt appears.
        [SkippableFact]
        public async Task ADocumentIdentificationInsideTheRaceWindowIsStillAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var gate = new ReceiptLookupGate();
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, mobileServices: s => s.AddReceiptLookupGate(gate));
            var (_, payload) = await PatientWithQrAsync(host, "Alba");
            var byDocument = await host.Web.SeedPatientAsync("Luis", "Gómez", Document);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            var key = Guid.NewGuid();

            gate.Arm(EmergencyQrIdentificationService.ConfirmOperation, key);
            var loser = ConfirmAsync(paramedic, emergency.Id, payload, key);
            await gate.Reached;
            Assert.Equal(HttpStatusCode.OK, (await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergency.Id}/identify/document",
                new { clientRequestId = Guid.NewGuid(), documentType = "NationalId", documentNumber = Document })).StatusCode);
            gate.Release();

            await AssertConflictAsync(await loser, payload);
            Assert.Equal(byDocument.Id, (await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Equal(IdentificationMethod.NationalId, Assert.Single(await SuccessfulAttemptsAsync(host, emergency.Id)).Method);
            Assert.Equal(0, await QrReceiptCountAsync(host));
            Assert.Equal(1, await IdentifyAuditsAsync(host, emergency.Id));
        }

        // A DIFFERENT QR request (same QR, another clientRequestId) winning inside that window has no receipt under the
        // loser's key: 409, not a replay; only the winner's receipt exists.
        [SkippableFact]
        public async Task AnotherQrRequestInsideTheRaceWindowIsStillAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var gate = new ReceiptLookupGate();
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, mobileServices: s => s.AddReceiptLookupGate(gate));
            var (patientId, payload) = await PatientWithQrAsync(host, "Alba");
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            var key = Guid.NewGuid();
            var winnerKey = Guid.NewGuid();

            gate.Arm(EmergencyQrIdentificationService.ConfirmOperation, key);
            var loser = ConfirmAsync(paramedic, emergency.Id, payload, key);
            await gate.Reached;
            Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(paramedic, emergency.Id, payload, winnerKey)).StatusCode);
            gate.Release();

            await AssertConflictAsync(await loser, payload);
            Assert.Equal(patientId, (await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Single(await SuccessfulAttemptsAsync(host, emergency.Id));
            Assert.Equal((0, 1, 1), (await QrReceiptCountAsync(host, key), await QrReceiptCountAsync(host, winnerKey), await QrReceiptCountAsync(host)));
        }

        // A true double submit without any gate: whichever way the two requests interleave, both answer 200 with the same
        // identified emergency, and exactly one identification, successful attempt and receipt exist.
        [SkippableFact]
        public async Task AConcurrentSameKeyDoubleSubmitIdentifiesOnce()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (patientId, payload) = await PatientWithQrAsync(host, "Alba");
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            var key = Guid.NewGuid();

            var responses = await Task.WhenAll(ConfirmAsync(paramedic, emergency.Id, payload, key), ConfirmAsync(paramedic, emergency.Id, payload, key));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            foreach (var response in responses)
            {
                var summary = await response.ReadAsAsync<PrehospitalEmergencySummary>();
                Assert.Equal((emergency.Id, emergency.TemporaryPatientCode, patientId), (summary.Id, summary.TemporaryPatientCode, summary.Patient!.Id));
            }

            Assert.Equal((Guid?)patientId, (await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Single(await SuccessfulAttemptsAsync(host, emergency.Id));
            Assert.Equal(1, await QrReceiptCountAsync(host, key));
            Assert.Equal(1, await IdentifyAuditsAsync(host, emergency.Id));
        }

        // Once the hospital received the emergency, a QR confirmation is a 409 and writes nothing: no identification, no
        // attempt, no receipt.
        [SkippableFact]
        public async Task AfterTheHandoffAQrConfirmationIsRejected()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (_, payload) = await PatientWithQrAsync(host, "Alba");
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital receptor");
            var paramedic = await ParamedicAsync(host, baseClinic);
            var doctor = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospital], staffType: ClinicalStaffType.Physician);
            var emergency = await OpenAsync(paramedic);
            var requested = await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergency.Id}/transfers",
                new { clientRequestId = Guid.NewGuid(), destinationClinicId = hospital.Id });
            Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
            using var json = JsonDocument.Parse(await requested.Content.ReadAsStringAsync());
            var transferId = json.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await paramedic.Client.PostAsync($"{Emergencies}/{emergency.Id}/transfers/{transferId}/start", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await doctor.Client.PostAsync($"/api/web/transfers/{transferId}/receive", null)).StatusCode);

            await AssertConflictAsync(await ConfirmAsync(paramedic, emergency.Id, payload, Guid.NewGuid()), payload);

            Assert.Null((await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Empty(await SuccessfulAttemptsAsync(host, emergency.Id));
            Assert.Equal(0, await QrReceiptCountAsync(host));
        }
    }
}
