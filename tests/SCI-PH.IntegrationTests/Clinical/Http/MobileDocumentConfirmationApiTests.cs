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
using SCI_PH.Domain.Enums.Transport;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // The identification by document in two steps through the Mobile BFF over PostgreSQL: resolve returns a minimized
    // candidate and leaves the emergency NN; only the confirmation (POST .../identify/document, the existing contract)
    // identifies the SAME emergency, resolving the document again, with one successful IdentificationAttempt (NationalId or
    // Passport). Misses are the generic 404 and a failed attempt without document; both steps share a per-user limit.
    [Collection(PostgresCollection.Name)]
    public sealed class MobileDocumentConfirmationApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private const string Number = "001-2468135-7";
        private const string Normalized = "00124681357";
        private readonly PostgresFixture _postgres;

        public MobileDocumentConfirmationApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static string ResolveUrl(Guid emergencyId) => $"{Emergencies}/{emergencyId}/identify/document/resolve";

        private static string ConfirmUrl(Guid emergencyId) => $"{Emergencies}/{emergencyId}/identify/document";

        private static Task<HttpResponseMessage> ResolveAsync(StaffSession staff, Guid emergencyId, string number = Number, string type = "NationalId") =>
            staff.Client.PostAsJsonAsync(ResolveUrl(emergencyId), new { documentType = type, documentNumber = number });

        private static Task<HttpResponseMessage> ConfirmAsync(
            StaffSession staff, Guid emergencyId, string number = Number, string type = "NationalId", Guid? clientRequestId = null) =>
            staff.Client.PostAsJsonAsync(ConfirmUrl(emergencyId), new { clientRequestId = clientRequestId ?? Guid.NewGuid(), documentType = type, documentNumber = number });

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

        private static async Task<StaffSession> ParamedicAsync(MobileEmergencyHost host, Domain.Entities.Organizations.Clinic? clinic = null) =>
            await host.SignInAsync(SecurityRoles.Paramedic, [clinic ?? await host.Web.CreateClinicAsync()]);

        private static async Task<Patient> SeedPatientAsync(
            MobileEmergencyHost host, PatientIdentifierType type = PatientIdentifierType.NationalId, string value = Number, string givenNames = "Marta")
        {
            var patient = Patient.Register(givenNames, "Rosario", new DateOnly(1979, 11, 2), BiologicalSex.Female);
            patient.AddIdentifier(type, value, PatientIdentifierSource.Manual, isPrimary: true);
            if (type != PatientIdentifierType.Passport)
            {
                // A second document the candidate must never reveal.
                patient.AddIdentifier(PatientIdentifierType.Passport, $"PX{Random.Shared.Next(1_000_000, 9_999_999)}", PatientIdentifierSource.Manual);
            }
            await host.Web.DbAsync(async db =>
            {
                db.Patients.Add(patient);
                await db.SaveChangesAsync();
            });
            return patient;
        }

        private static Task<List<IdentificationAttempt>> AttemptsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.IdentificationAttempts.AsNoTracking().Where(a => a.ClinicalEncounterId == emergencyId).ToListAsync());

        private static Task<Domain.Entities.Encounters.ClinicalEncounter> EncounterAsync(MobileEmergencyHost host, Guid id) =>
            host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == id));

        private static async Task<Guid> StartTransportAsync(StaffSession paramedic, Guid emergencyId, Guid destination)
        {
            var requested = await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergencyId}/transfers",
                new { clientRequestId = Guid.NewGuid(), destinationClinicId = destination });
            Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
            using var json = JsonDocument.Parse(await requested.Content.ReadAsStringAsync());
            var transferId = json.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await paramedic.Client.PostAsync($"{Emergencies}/{emergencyId}/transfers/{transferId}/start", null)).StatusCode);
            return transferId;
        }

        // The document, normalized or as typed, never reaches the audit, an attempt or a log.
        private static async Task AssertDocumentNotStoredAsync(MobileEmergencyHost host)
        {
            foreach (var entry in await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking().ToListAsync()))
            {
                foreach (var text in new[] { entry.EntityId, entry.EntityName, entry.ChangesJson, entry.FailureReason })
                {
                    Assert.DoesNotContain(Normalized, text ?? string.Empty);
                    Assert.DoesNotContain(Number, text ?? string.Empty);
                }
            }

            foreach (var attempt in await host.Web.DbAsync(db => db.IdentificationAttempts.AsNoTracking().ToListAsync()))
            {
                Assert.DoesNotContain(Normalized, $"{attempt.DeviceReference}{attempt.Provider}{attempt.ProviderRequestReference}");
            }

            foreach (var receipt in await host.Web.DbAsync(db => db.IdempotencyReceipts.AsNoTracking().ToListAsync()))
            {
                Assert.DoesNotContain(Normalized, receipt.RequestHash);
            }

            foreach (var logs in new[] { host.Logs.Text, host.Web.Logs.Text })
            {
                Assert.DoesNotContain(Normalized, logs);
                Assert.DoesNotContain(Number, logs);
            }
        }

        private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(Normalized, body);
            Assert.DoesNotContain(Number, body);
        }

        [SkippableFact]
        public async Task ResolvingShowsAMinimizedCandidateAndChangesNothing()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var patient = await SeedPatientAsync(host);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);

            var response = await ResolveAsync(paramedic, emergency.Id, " 001 2468135 7 ");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var raw = await response.Content.ReadAsStringAsync();
            using (var json = JsonDocument.Parse(raw))
            {
                Assert.Equal(["dateOfBirth", "displayName", "document"], json.RootElement.EnumerateObject().Select(p => p.Name).Order());
                Assert.Equal(["type", "value"], json.RootElement.GetProperty("document").EnumerateObject().Select(p => p.Name).Order());
                Assert.Equal("Marta Rosario", json.RootElement.GetProperty("displayName").GetString());
                Assert.Equal("1979-11-02", json.RootElement.GetProperty("dateOfBirth").GetString());
                Assert.Equal("NationalId", json.RootElement.GetProperty("document").GetProperty("type").GetString());
                var masked = json.RootElement.GetProperty("document").GetProperty("value").GetString()!;
                Assert.StartsWith("***", masked);
                Assert.EndsWith("1357", masked);
            }

            var identifierId = patient.Identifiers.Single(i => i.Type == PatientIdentifierType.NationalId).Id;
            foreach (var hidden in new[] { patient.Id.ToString(), identifierId.ToString(), Normalized, "PX" })
            {
                Assert.DoesNotContain(hidden, raw);
            }

            // Still NN: no identification, no attempt, no receipt.
            var encounter = await EncounterAsync(host, emergency.Id);
            Assert.Null(encounter.PatientId);
            Assert.Equal(emergency.TemporaryPatientCode, encounter.TemporaryPatientCode!.Value);
            Assert.Empty(await AttemptsAsync(host, emergency.Id));
            Assert.Equal(0, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == EmergencyDocumentIdentificationService.ConfirmOperation)));
            Assert.Equal(0, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergency.Id && a.Action == AuditActionType.Identify)));

            // Candidate discovery: one Read audit entry on the identifier, by this staff member.
            var audit = await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .SingleAsync(a => a.EntityName == EmergencyDocumentIdentificationService.CandidateAuditResource));
            Assert.Equal(
                (AuditActionType.Read, AuditOutcome.Success, identifierId.ToString(), (Guid?)patient.Id, (Guid?)emergency.Id, (Guid?)paramedic.UserId, paramedic.StaffId),
                (audit.Action, audit.Outcome, audit.EntityId, audit.PatientId, audit.ClinicalEncounterId, audit.ActorIdentityUserId, audit.ActorStaffId));
            await AssertDocumentNotStoredAsync(host);
        }

        [SkippableTheory]
        [InlineData("NationalId", IdentificationMethod.NationalId)]
        [InlineData("Passport", IdentificationMethod.Passport)]
        public async Task ConfirmingIdentifiesTheSameEmergencyOnceAndAReplayChangesNothing(string type, IdentificationMethod method)
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var number = type == "NationalId" ? Number : "RD9081726";
            var patient = await SeedPatientAsync(host, Enum.Parse<PatientIdentifierType>(type), number);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(paramedic, emergency.Id, number, type)).StatusCode);
            var key = Guid.NewGuid();

            var confirmed = await ConfirmAsync(paramedic, emergency.Id, number, type, key);
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            var result = await confirmed.ReadAsAsync<PrehospitalEmergencySummary>();
            // The same request again, retried after an ambiguous network result: the same answer, nothing new.
            var replay = await ConfirmAsync(paramedic, emergency.Id, number, type, key);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            var again = await replay.ReadAsAsync<PrehospitalEmergencySummary>();

            Assert.Equal((emergency.Id, emergency.TemporaryPatientCode, patient.Id), (result.Id, result.TemporaryPatientCode, result.Patient!.Id));
            Assert.Equal((result.Id, result.Patient.Id), (again.Id, again.Patient!.Id));
            var encounter = await EncounterAsync(host, emergency.Id);
            Assert.Equal(((Guid?)patient.Id, emergency.TemporaryPatientCode), (encounter.PatientId, encounter.TemporaryPatientCode!.Value));
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalEncounters.CountAsync(e => e.TemporaryPatientCode != null
                && e.Id == emergency.Id)));

            var attempt = Assert.Single(await AttemptsAsync(host, emergency.Id));
            var identifierId = patient.Identifiers.Single(i => i.Type == Enum.Parse<PatientIdentifierType>(type)).Id;
            Assert.Equal(
                (method, IdentificationOutcome.Success, (Guid?)patient.Id, (Guid?)identifierId, paramedic.StaffId, (Guid?)paramedic.UserId),
                (attempt.Method, attempt.Outcome, attempt.PatientId, attempt.PatientIdentifierId, attempt.PerformedByStaffId, attempt.ActorIdentityUserId));
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergency.Id && a.Action == AuditActionType.Identify)));
            Assert.Equal(1, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == EmergencyDocumentIdentificationService.ConfirmOperation)));
            await AssertDocumentNotStoredAsync(host);
        }

        [SkippableFact]
        public async Task AnUnknownDocumentIsTheGenericNotFoundAtBothStepsAndOnlyAMissIsRecorded()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);

            await AssertProblemAsync(await ResolveAsync(paramedic, emergency.Id), HttpStatusCode.NotFound);
            await AssertProblemAsync(await ConfirmAsync(paramedic, emergency.Id), HttpStatusCode.NotFound);

            Assert.Null((await EncounterAsync(host, emergency.Id)).PatientId);
            var misses = await AttemptsAsync(host, emergency.Id);
            Assert.Equal(2, misses.Count);
            Assert.All(misses, a => Assert.Equal(
                (IdentificationMethod.NationalId, IdentificationOutcome.Failure, (IdentificationFailureReason?)IdentificationFailureReason.NoMatch, (Guid?)null, (Guid?)null),
                (a.Method, a.Outcome, a.FailureReason, a.PatientId, a.PatientIdentifierId)));
            Assert.Equal(0, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == EmergencyDocumentIdentificationService.ConfirmOperation)));
            Assert.Equal(0, await host.Web.DbAsync(db => db.Patients.CountAsync(p => p.Identifiers.Any(i => i.NormalizedValue == Normalized))));
            await AssertDocumentNotStoredAsync(host);
        }

        // Access to the emergency before the lookup: another paramedic of the same base clinic gets the same 403 whether or
        // not the document exists, and leaves no attempt.
        [SkippableFact]
        public async Task AnotherParamedicIsForbiddenAtBothStepsAndLearnsNothing()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            await SeedPatientAsync(host);
            var clinic = await host.Web.CreateClinicAsync();
            var owner = await ParamedicAsync(host, clinic);
            var other = await ParamedicAsync(host, clinic);
            var emergency = await OpenAsync(owner);

            await AssertProblemAsync(await ResolveAsync(other, emergency.Id), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await ResolveAsync(other, emergency.Id, "001-0000000-1"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await ConfirmAsync(other, emergency.Id), HttpStatusCode.Forbidden);

            Assert.Null((await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Empty(await AttemptsAsync(host, emergency.Id));
            Assert.Equal(0, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.EntityName == EmergencyDocumentIdentificationService.CandidateAuditResource)));
        }

        // No patient id, no unknown field, no other document type, the document only in the body.
        [SkippableFact]
        public async Task ForgedOrInvalidInputIsRejectedWithoutTouchingTheEmergency()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var patient = await SeedPatientAsync(host);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);

            var bad = new[]
            {
                await paramedic.Client.PostAsJsonAsync(ResolveUrl(emergency.Id), new { documentType = "NationalId", documentNumber = Number, patientId = patient.Id }),
                await paramedic.Client.PostAsJsonAsync(ConfirmUrl(emergency.Id), new { clientRequestId = Guid.NewGuid(), documentType = "NationalId", documentNumber = Number, patientId = patient.Id }),
                await paramedic.Client.PostAsJsonAsync(ResolveUrl(emergency.Id), new { documentType = "NationalId" }),
                await paramedic.Client.PostAsJsonAsync(ConfirmUrl(emergency.Id), new { documentType = "NationalId", documentNumber = Number }),
                await ResolveAsync(paramedic, emergency.Id, Number, "InternalCode"),
                await ConfirmAsync(paramedic, emergency.Id, Number, "Other"),
                await ResolveAsync(paramedic, emergency.Id, "--- ..."),
            };
            foreach (var response in bad)
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.DoesNotContain(Normalized, await response.Content.ReadAsStringAsync());
            }

            Assert.Equal(HttpStatusCode.UnsupportedMediaType,
                (await paramedic.Client.PostAsync($"{ResolveUrl(emergency.Id)}?documentType=NationalId&documentNumber={Normalized}", null)).StatusCode);
            Assert.Null((await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Empty(await AttemptsAsync(host, emergency.Id));
            await AssertDocumentNotStoredAsync(host);
        }

        [SkippableFact]
        public async Task BothStepsShareAPerUserLimit()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, new Dictionary<string, string?> { ["RateLimiting:PatientDocumentResolvePermitLimit"] = "2" });
            await SeedPatientAsync(host);
            var clinic = await host.Web.CreateClinicAsync();
            var first = await ParamedicAsync(host, clinic);
            var second = await ParamedicAsync(host, clinic);
            var firstEmergency = await OpenAsync(first);
            var secondEmergency = await OpenAsync(second);

            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(first, firstEmergency.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync(first, firstEmergency.Id, "001-0000000-1")).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await ResolveAsync(first, firstEmergency.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await ConfirmAsync(first, firstEmergency.Id)).StatusCode);
            Assert.Null((await EncounterAsync(host, firstEmergency.Id)).PatientId);

            // Not shared among staff, and the rest of the emergency API goes on.
            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(second, secondEmergency.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await first.Client.GetAsync($"{Emergencies}/{firstEmergency.Id}")).StatusCode);
        }

        [SkippableFact]
        public async Task AnIdentifiedEmergencyRejectsBothStepsAndKeepsItsPatient()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var first = await SeedPatientAsync(host);
            await SeedPatientAsync(host, PatientIdentifierType.NationalId, "001-1357924-6", "Otra");
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(paramedic, emergency.Id)).StatusCode);

            await AssertProblemAsync(await ResolveAsync(paramedic, emergency.Id, "001-1357924-6"), HttpStatusCode.Conflict);
            await AssertProblemAsync(await ConfirmAsync(paramedic, emergency.Id, "001-1357924-6"), HttpStatusCode.Conflict);

            Assert.Equal(first.Id, (await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Single(await AttemptsAsync(host, emergency.Id));
        }

        // Cross-method: candidate A resolved by document, then the QR identifies patient B, then the document confirmation
        // arrives: 409, no overwrite, one final identity and one successful attempt (the QR's).
        [SkippableFact]
        public async Task ADocumentConfirmationAfterAQrIdentificationDoesNotOverwrite()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            await SeedPatientAsync(host);
            var (qrRecord, qrPatient) = await host.SignInPatientAsync("Alba");
            var issued = await qrPatient.PostAsync("/api/mobile/patient/me/qr", null);
            Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
            using var credential = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
            var payload = credential.RootElement.GetProperty("payload").GetString()!;
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);

            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(paramedic, emergency.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await paramedic.Client.PostAsJsonAsync(
                $"{Emergencies}/{emergency.Id}/identify/qr/confirm", new { clientRequestId = Guid.NewGuid(), payload })).StatusCode);

            await AssertProblemAsync(await ConfirmAsync(paramedic, emergency.Id), HttpStatusCode.Conflict);

            Assert.Equal(qrRecord.PatientId, (await EncounterAsync(host, emergency.Id)).PatientId);
            var success = Assert.Single(await AttemptsAsync(host, emergency.Id), a => a.Outcome == IdentificationOutcome.Success);
            Assert.Equal(IdentificationMethod.QrCode, success.Method);
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergency.Id && a.Action == AuditActionType.Identify)));
        }

        // NN in transport: resolve + confirm keep the SAME transfer in its status; after the hospital received the case both
        // steps are a 409 (also for a paramedic affiliated with the receiving clinic) and nothing is written.
        [SkippableTheory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task OnTheWayTheTransferIsKeptAndAfterTheHandoffBothStepsAreRejected(bool paramedicAffiliatedWithDestination)
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var patient = await SeedPatientAsync(host);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = paramedicAffiliatedWithDestination ? baseClinic : await host.Web.CreateClinicAsync("Hospital receptor");
            var paramedic = await ParamedicAsync(host, baseClinic);
            var doctor = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospital], staffType: ClinicalStaffType.Physician);

            var onTheWay = await OpenAsync(paramedic);
            var transferId = await StartTransportAsync(paramedic, onTheWay.Id, hospital.Id);
            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(paramedic, onTheWay.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(paramedic, onTheWay.Id)).StatusCode);
            var current = await (await paramedic.Client.GetAsync($"{Emergencies}/{onTheWay.Id}")).ReadAsAsync<PrehospitalEmergencySummary>();
            Assert.Equal((patient.Id, onTheWay.TemporaryPatientCode, transferId, ClinicalTransferStatus.InTransit),
                (current.Patient!.Id, current.TemporaryPatientCode, current.ActiveTransfer!.Id, current.ActiveTransfer.Status));
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalTransfers.CountAsync(t => t.ClinicalEncounterId == onTheWay.Id)));

            var received = await OpenAsync(paramedic);
            var receivedTransfer = await StartTransportAsync(paramedic, received.Id, hospital.Id);
            Assert.Equal(HttpStatusCode.OK, (await doctor.Client.PostAsync($"/api/web/transfers/{receivedTransfer}/receive", null)).StatusCode);

            await AssertProblemAsync(await ResolveAsync(paramedic, received.Id), HttpStatusCode.Conflict);
            await AssertProblemAsync(await ConfirmAsync(paramedic, received.Id), HttpStatusCode.Conflict);
            Assert.Null((await EncounterAsync(host, received.Id)).PatientId);
            Assert.Empty(await AttemptsAsync(host, received.Id));
        }
    }
}
