using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.ValueObjects;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Clinical.Idempotency;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Real identification by document of a prehospital emergency through the Mobile BFF over PostgreSQL: exact match in
    // the platform scope (empty issuer), the SAME encounter linked to the found patient (temporary code kept, one
    // Identify audit entry, one receipt), never a new patient; replay, conflicts and races without duplicates or 500s.
    [Collection(PostgresCollection.Name)]
    public sealed class MobileDocumentIdentificationApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private readonly PostgresFixture _postgres;

        public MobileDocumentIdentificationApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static string IdentifyUrl(Guid emergencyId) => $"{Emergencies}/{emergencyId}/identify/document";

        private static object Body(Guid clientRequestId, string documentType, string documentNumber) =>
            new { clientRequestId, documentType, documentNumber };

        private static Task<HttpResponseMessage> IdentifyAsync(
            HttpClient client, Guid emergencyId, string documentNumber, string documentType = "NationalId", Guid? clientRequestId = null) =>
            client.PostAsJsonAsync(IdentifyUrl(emergencyId), Body(clientRequestId ?? Guid.NewGuid(), documentType, documentNumber));

        private static async Task<PrehospitalEmergencySummary> OpenAsync(StaffSession paramedic)
        {
            var response = await paramedic.Client.PostAsJsonAsync(Emergencies, new
            {
                clientRequestId = Guid.NewGuid(),
                startedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                chiefComplaint = "Dolor torácico"
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<PrehospitalEmergencySummary>();
        }

        private static async Task<Patient> SeedPatientAsync(
            MobileEmergencyHost host, PatientIdentifierType type, string value, string? issuer = null, string givenNames = "Ana")
        {
            var patient = Patient.Register(givenNames, "Pérez", new DateOnly(1985, 3, 14), BiologicalSex.Female);
            patient.AddIdentifier(type, value, PatientIdentifierSource.Manual, issuer, isPrimary: true);
            await host.Web.DbAsync(async db =>
            {
                db.Patients.Add(patient);
                await db.SaveChangesAsync();
            });
            return patient;
        }

        private static Task<int> IdentifyAuditsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergencyId && a.Action == AuditActionType.Identify));

        private static Task<int> IdentifyReceiptsAsync(MobileEmergencyHost host) =>
            host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == PrehospitalEmergencyService.IdentifyByDocumentOperation));

        [SkippableFact]
        public async Task TheAssignedParamedicIdentifiesTheSameEmergencyByNationalIdAndByPassport()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var byNationalId = await SeedPatientAsync(host, PatientIdentifierType.NationalId, "402-1234567-8");
            var byPassport = await SeedPatientAsync(host, PatientIdentifierType.Passport, "RD 123 4567", givenNames: "Luis");

            foreach (var (patient, type, typed) in new[]
            {
                (byNationalId, "NationalId", "40212345678"),
                (byPassport, "Passport", "rd-1234567")
            })
            {
                var opened = await OpenAsync(paramedic);
                var encountersBefore = await host.Web.DbAsync(db => db.ClinicalEncounters.CountAsync());
                var key = Guid.NewGuid();

                var response = await IdentifyAsync(paramedic.Client, opened.Id, typed, type, key);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var raw = await response.Content.ReadAsStringAsync();
                var identified = JsonSerializer.Deserialize<PrehospitalEmergencySummary>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Assert.Equal((opened.Id, opened.TemporaryPatientCode, patient.Id), (identified.Id, identified.TemporaryPatientCode, identified.Patient!.Id));
                // The number never comes back: the summary masks it.
                Assert.DoesNotContain(PatientIdentifier.Normalize(Enum.Parse<PatientIdentifierType>(type), typed), raw);

                var encounter = await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == opened.Id));
                Assert.Equal(patient.Id, encounter.PatientId);
                Assert.Equal(opened.TemporaryPatientCode, encounter.TemporaryPatientCode!.Value);
                Assert.Null(encounter.ClinicId);
                Assert.Equal((ClinicalEncounterType.Prehospital, ClinicalEncounterStatus.Created), (encounter.Type, encounter.Status));
                Assert.Equal(paramedic.StaffId, encounter.IdentifiedByStaffId);
                Assert.NotNull(encounter.IdentifiedAtUtc);
                Assert.Equal(encountersBefore, await host.Web.DbAsync(db => db.ClinicalEncounters.CountAsync()));

                var audit = await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                    .SingleAsync(a => a.ClinicalEncounterId == opened.Id && a.Action == AuditActionType.Identify));
                Assert.Equal((patient.Id, paramedic.UserId, paramedic.StaffId), (audit.PatientId, audit.ActorIdentityUserId, audit.ActorStaffId));
                Assert.DoesNotContain(PatientIdentifier.Normalize(Enum.Parse<PatientIdentifierType>(type), typed), audit.ChangesJson);

                var receipt = await host.Web.DbAsync(db => db.IdempotencyReceipts.AsNoTracking().SingleAsync(r => r.ClientRequestId == key));
                Assert.Equal((PrehospitalEmergencyService.IdentifyByDocumentOperation, paramedic.UserId, opened.Id),
                    (receipt.Operation, receipt.ActorUserId, receipt.ResourceId));
                Assert.Matches("^[0-9a-f]{64}$", receipt.RequestHash);
            }

            Assert.Equal(2, await host.Web.DbAsync(db => db.Patients.CountAsync()));
        }

        [SkippableFact]
        public async Task OnlyNationalIdAndPassportWithANumberAreAccepted()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic);
            await SeedPatientAsync(host, PatientIdentifierType.InternalCode, "SCI-0001");

            foreach (var body in new object[]
            {
                Body(Guid.NewGuid(), "Other", "SCI-0001"),
                Body(Guid.NewGuid(), "QrCode", "SCI-0001"),
                Body(Guid.NewGuid(), "InternalCode", "SCI-0001"),
                Body(Guid.NewGuid(), "Cédula", "SCI-0001"),
                Body(Guid.NewGuid(), "nationalid-x", "SCI-0001"),
                new { clientRequestId = Guid.NewGuid(), documentType = 1, documentNumber = "SCI-0001" },
                Body(Guid.NewGuid(), "NationalId", ""),
                Body(Guid.NewGuid(), "NationalId", " - . "),
                Body(Guid.Empty, "NationalId", "SCI-0001"),
                new { clientRequestId = Guid.NewGuid(), documentType = "NationalId" },
                new { clientRequestId = Guid.NewGuid(), documentType = "NationalId", documentNumber = "1", issuer = "DO" }
            })
            {
                await AssertProblemAsync(await paramedic.Client.PostAsJsonAsync(IdentifyUrl(opened.Id), body), HttpStatusCode.BadRequest);
            }

            Assert.Null(await host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == opened.Id).Select(e => e.PatientId).SingleAsync()));
            Assert.Equal(0, await IdentifyReceiptsAsync(host));
            Assert.Equal(0, await IdentifyAuditsAsync(host, opened.Id));
        }

        // D1: the match is exact in the platform scope (empty issuer). The same value under another issuer is never used.
        [SkippableFact]
        public async Task OnlyThePlatformScopeIsSearched()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            await SeedPatientAsync(host, PatientIdentifierType.Passport, "PA7654321", issuer: "DO");
            var opened = await OpenAsync(paramedic);

            var notFound = await IdentifyAsync(paramedic.Client, opened.Id, "PA7654321", "Passport");

            var problem = await AssertProblemAsync(notFound, HttpStatusCode.NotFound);
            Assert.DoesNotContain("7654321", problem);
            Assert.Null(await host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == opened.Id).Select(e => e.PatientId).SingleAsync()));
            Assert.Equal(0, await IdentifyReceiptsAsync(host));
            Assert.Equal(0, await IdentifyAuditsAsync(host, opened.Id));

            // With the same value in the platform scope too, that one is chosen, deterministically.
            var platform = await SeedPatientAsync(host, PatientIdentifierType.Passport, "PA7654321", givenNames: "Platform");
            var found = await IdentifyAsync(paramedic.Client, opened.Id, "PA7654321", "Passport");
            Assert.Equal(HttpStatusCode.OK, found.StatusCode);
            Assert.Equal(platform.Id, (await found.ReadAsAsync<PrehospitalEmergencySummary>()).Patient!.Id);
        }

        // Not found writes nothing (no receipt, no patient, no audit), so the same request works once the patient exists.
        [SkippableFact]
        public async Task AnUnknownDocumentLeavesTheEmergencyNnAndCreatesNoPatient()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic);
            var key = Guid.NewGuid();

            await AssertProblemAsync(await IdentifyAsync(paramedic.Client, opened.Id, "001-7777777-1", clientRequestId: key), HttpStatusCode.NotFound);

            var encounter = await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == opened.Id));
            Assert.Null(encounter.PatientId);
            Assert.Equal(opened.TemporaryPatientCode, encounter.TemporaryPatientCode!.Value);
            Assert.Equal(0, await host.Web.DbAsync(db => db.Patients.CountAsync()));
            Assert.Equal(0, await IdentifyReceiptsAsync(host));

            var patient = await SeedPatientAsync(host, PatientIdentifierType.NationalId, "001-7777777-1");
            var retried = await IdentifyAsync(paramedic.Client, opened.Id, "001-7777777-1", clientRequestId: key);
            Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
            Assert.Equal(patient.Id, (await retried.ReadAsAsync<PrehospitalEmergencySummary>()).Patient!.Id);
        }

        [SkippableFact]
        public async Task OnlyTheAssignedParamedicIdentifiesTheEmergency()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var clinic = await host.Web.CreateClinicAsync();
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var otherParamedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            var receptionist = await host.SignInAsync(SecurityRoles.Receptionist, [clinic], staffType: ClinicalStaffType.OtherClinical);
            var patient = await SeedPatientAsync(host, PatientIdentifierType.NationalId, "001-1234567-8");
            var opened = await OpenAsync(paramedic);

            Assert.Equal(HttpStatusCode.Unauthorized, (await IdentifyAsync(host.Anonymous(), opened.Id, "001-1234567-8")).StatusCode);
            // Doctor and receptionist: no Emergencies.Operate. Another paramedic: not assigned to this emergency.
            await AssertProblemAsync(await IdentifyAsync(doctor.Client, opened.Id, "001-1234567-8"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await IdentifyAsync(receptionist.Client, opened.Id, "001-1234567-8"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await IdentifyAsync(otherParamedic.Client, opened.Id, "001-1234567-8"), HttpStatusCode.Forbidden);
            // An unassigned paramedic learns nothing about the document: 403 even for an unknown one.
            await AssertProblemAsync(await IdentifyAsync(otherParamedic.Client, opened.Id, "999-9999999-9"), HttpStatusCode.Forbidden);
            Assert.Null(await host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == opened.Id).Select(e => e.PatientId).SingleAsync()));

            var response = await IdentifyAsync(paramedic.Client, opened.Id, "001-1234567-8");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(patient.Id, (await response.ReadAsAsync<PrehospitalEmergencySummary>()).Patient!.Id);
            Assert.Equal(1, await IdentifyAuditsAsync(host, opened.Id));
            await AssertProblemAsync(await IdentifyAsync(paramedic.Client, Guid.NewGuid(), "001-1234567-8"), HttpStatusCode.NotFound);
        }

        // Same key + same logical document (another spelling included): the same result, nothing new. Same key with another
        // document, type or emergency: 409. A new key on an identified emergency: 409, even for the same patient (D4).
        [SkippableFact]
        public async Task RetryingReplaysAndEveryOtherRequestIsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var patient = await SeedPatientAsync(host, PatientIdentifierType.NationalId, "001-1234567-8");
            await SeedPatientAsync(host, PatientIdentifierType.NationalId, "001-7654321-0", givenNames: "Otra");
            await SeedPatientAsync(host, PatientIdentifierType.Passport, "00112345678", givenNames: "Pasaporte");
            var opened = await OpenAsync(paramedic);
            var other = await OpenAsync(paramedic);
            var key = Guid.NewGuid();

            var first = await IdentifyAsync(paramedic.Client, opened.Id, "001-1234567-8", clientRequestId: key);
            var replay = await IdentifyAsync(paramedic.Client, opened.Id, " 001 1234567 8 ", clientRequestId: key);

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, replay.StatusCode));
            var (a, b) = (await first.ReadAsAsync<PrehospitalEmergencySummary>(), await replay.ReadAsAsync<PrehospitalEmergencySummary>());
            Assert.Equal((a.Id, a.Patient!.Id), (b.Id, b.Patient!.Id));
            Assert.Equal(patient.Id, a.Patient.Id);

            await AssertProblemAsync(await IdentifyAsync(paramedic.Client, opened.Id, "001-7654321-0", clientRequestId: key), HttpStatusCode.Conflict);
            await AssertProblemAsync(await IdentifyAsync(paramedic.Client, opened.Id, "00112345678", "Passport", key), HttpStatusCode.Conflict);
            await AssertProblemAsync(await IdentifyAsync(paramedic.Client, other.Id, "001-1234567-8", clientRequestId: key), HttpStatusCode.Conflict);
            await AssertProblemAsync(await IdentifyAsync(paramedic.Client, opened.Id, "001-1234567-8"), HttpStatusCode.Conflict);

            Assert.Equal(patient.Id, await host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == opened.Id).Select(e => e.PatientId).SingleAsync()));
            Assert.Null(await host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == other.Id).Select(e => e.PatientId).SingleAsync()));
            Assert.Equal(1, await IdentifyAuditsAsync(host, opened.Id));
            Assert.Equal(0, await IdentifyAuditsAsync(host, other.Id));
            Assert.Equal(1, await IdentifyReceiptsAsync(host));
        }

        [SkippableFact]
        public async Task AConcurrentDoubleSubmitIdentifiesOnce()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var patient = await SeedPatientAsync(host, PatientIdentifierType.NationalId, "001-1234567-8");

            for (var round = 0; round < 5; round++)
            {
                var opened = await OpenAsync(paramedic);
                var body = Body(Guid.NewGuid(), "NationalId", "001-1234567-8");
                using var start = new ManualResetEventSlim();
                Task<HttpResponseMessage> Submit() => Task.Run(async () => { start.Wait(); return await paramedic.Client.PostAsJsonAsync(IdentifyUrl(opened.Id), body); });
                var tasks = new[] { Submit(), Submit() };
                start.Set();
                var responses = await Task.WhenAll(tasks);

                Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
                var results = await Task.WhenAll(responses.Select(r => r.ReadAsAsync<PrehospitalEmergencySummary>()));
                Assert.All(results, r => Assert.Equal((opened.Id, patient.Id), (r.Id, r.Patient!.Id)));
                Assert.Equal(1, await IdentifyAuditsAsync(host, opened.Id));
            }

            Assert.Equal(5, await IdentifyReceiptsAsync(host));
        }

        // Deterministic race of the same request: another transaction holds its receipt and identification uncommitted.
        // The request waits on them and, when they commit, answers 200 with the winner's result: no second audit, no 500.
        [SkippableFact]
        public async Task LosingTheRaceToTheSameRequestReturnsTheWinner()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var patient = await SeedPatientAsync(host, PatientIdentifierType.NationalId, "001-1234567-8");
            var opened = await OpenAsync(paramedic);
            var key = Guid.NewGuid();

            await using var other = PostgresFixture.Clinical(host.ConnectionString);
            await using var transaction = await other.Database.BeginTransactionAsync();
            var encounter = await other.ClinicalEncounters.SingleAsync(e => e.Id == opened.Id);
            encounter.IdentifyPatient(patient.Id, paramedic.StaffId!.Value, DateTimeOffset.UtcNow);
            other.IdempotencyReceipts.Add(IdempotencyReceiptEntry.From(new IdempotencyReceipt(
                new IdempotencyKey(paramedic.UserId, PrehospitalEmergencyService.IdentifyByDocumentOperation, key),
                IdempotencyRequestHash.Compute(opened.Id.ToString("D"), "NationalId", "00112345678"), opened.Id, DateTimeOffset.UtcNow)));
            await other.SaveChangesAsync();

            var request = IdentifyAsync(paramedic.Client, opened.Id, "001-1234567-8", clientRequestId: key);
            await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
            await transaction.CommitAsync();
            var response = await request;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(patient.Id, (await response.ReadAsAsync<PrehospitalEmergencySummary>()).Patient!.Id);
            Assert.Equal(1, await IdentifyReceiptsAsync(host));
            Assert.Equal(0, await IdentifyAuditsAsync(host, opened.Id));
        }

        // Deterministic race of two patients: another transaction identifies the emergency with patient A, uncommitted.
        // The request for patient B reads it as NN, waits on the row and loses on the PatientId concurrency token: 409.
        [SkippableFact]
        public async Task TwoPatientsRacingLeaveExactlyOneWinner()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var winner = await SeedPatientAsync(host, PatientIdentifierType.NationalId, "001-1111111-1");
            await SeedPatientAsync(host, PatientIdentifierType.NationalId, "001-2222222-2", givenNames: "Perdedora");
            var opened = await OpenAsync(paramedic);

            await using var other = PostgresFixture.Clinical(host.ConnectionString);
            await using var transaction = await other.Database.BeginTransactionAsync();
            var encounter = await other.ClinicalEncounters.SingleAsync(e => e.Id == opened.Id);
            encounter.IdentifyPatient(winner.Id, paramedic.StaffId!.Value, DateTimeOffset.UtcNow);
            other.ClinicalAuditEntries.Add(ClinicalAuditEntry.Success(
                AuditActor.User(paramedic.UserId, paramedic.StaffId), AuditActionType.Identify, "ClinicalEncounter", opened.Id.ToString(),
                DateTimeOffset.UtcNow, new AuditContext(winner.Id, opened.Id)));
            await other.SaveChangesAsync();

            var request = IdentifyAsync(paramedic.Client, opened.Id, "001-2222222-2");
            await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
            await transaction.CommitAsync();

            await AssertProblemAsync(await request, HttpStatusCode.Conflict);
            var final = await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == opened.Id));
            Assert.Equal(winner.Id, final.PatientId);
            Assert.Equal(opened.TemporaryPatientCode, final.TemporaryPatientCode!.Value);
            Assert.Equal(1, await IdentifyAuditsAsync(host, opened.Id));
            Assert.Equal(0, await IdentifyReceiptsAsync(host));
        }

        private static async Task<string> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            using var problem = JsonDocument.Parse(body);
            Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
            return body;
        }
    }
}
