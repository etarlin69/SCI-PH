using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Identification of an NN emergency by fingerprint over the real hosts and PostgreSQL: the paramedic opens a session
    // with a reader (Mobile BFF), the reader polls for it and reports the sensor's match (Device API, device key), the
    // paramedic confirms the candidate. The reader never receives patient or clinical data; only the confirmation
    // identifies the emergency; sessions are bound to their emergency, reader and paramedic and expire after 90 s; results,
    // confirmations and races resolve to one winner without duplicates or 500s.
    [Collection(PostgresCollection.Name)]
    public sealed class FingerprintIdentificationApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private const string Readers = "/api/mobile/devices/fingerprint-readers";
        private const string Pending = "/api/device/fingerprint/sessions/pending";
        private const int Slot = 3;
        private readonly PostgresFixture _postgres;

        public FingerprintIdentificationApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static string Sessions(Guid emergencyId) => $"{Emergencies}/{emergencyId}/identify/fingerprint/sessions";

        private static string Result(Guid sessionId) => $"/api/device/fingerprint/sessions/{sessionId}/result";

        private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, string json) =>
            client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

        private static Task<HttpResponseMessage> OpenAsync(StaffSession paramedic, Guid emergencyId, Guid readerId, Guid? key = null) =>
            paramedic.Client.PostAsJsonAsync(Sessions(emergencyId), new { clientRequestId = key ?? Guid.NewGuid(), deviceId = readerId });

        private static async Task<JsonElement> OpenedAsync(StaffSession paramedic, Guid emergencyId, Guid readerId)
        {
            var response = await OpenAsync(paramedic, emergencyId, readerId);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await JsonAsync(response);
        }

        private static Task<HttpResponseMessage> SubmitAsync(HttpClient reader, Guid sessionId, string outcome, int? slot = null) =>
            PostJsonAsync(reader, Result(sessionId), slot is null ? $$"""{"outcome":"{{outcome}}"}""" : $$"""{"outcome":"{{outcome}}","slot":{{slot}}}""");

        private static Task<HttpResponseMessage> ConfirmAsync(StaffSession paramedic, Guid emergencyId, Guid sessionId, Guid? key = null) =>
            paramedic.Client.PostAsJsonAsync($"{Sessions(emergencyId)}/{sessionId}/confirm", new { clientRequestId = key ?? Guid.NewGuid() });

        private static Task<HttpResponseMessage> CancelAsync(StaffSession paramedic, Guid emergencyId, Guid sessionId) =>
            paramedic.Client.PostAsync($"{Sessions(emergencyId)}/{sessionId}/cancel", null);

        private static async Task<JsonElement> SessionAsync(StaffSession paramedic, Guid emergencyId, Guid sessionId)
        {
            var response = await paramedic.Client.GetAsync($"{Sessions(emergencyId)}/{sessionId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await JsonAsync(response);
        }

        private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        private static Guid Id(JsonElement session) => session.GetProperty("id").GetGuid();

        private static async Task<PrehospitalEmergencySummary> OpenEmergencyAsync(StaffSession paramedic)
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

        // A patient whose fingerprint is in that slot of that reader (the enrollment does not exist yet).
        private static async Task<(Patient Patient, PatientBiometricReference Reference)> EnrolledPatientAsync(
            MobileEmergencyHost host, TestReader reader, int slot = Slot, string givenNames = "Ana")
        {
            var patient = Patient.Register(givenNames, "Pérez", new DateOnly(1985, 3, 14), BiologicalSex.Female);
            var reference = PatientBiometricReference.Register(
                patient.Id, BiometricModality.Fingerprint, FingerprintReferences.Provider, FingerprintReferences.SubjectReference(reader.Id, slot));
            await host.Web.DbAsync(async db =>
            {
                db.Patients.Add(patient);
                db.PatientBiometricReferences.Add(reference);
                await db.SaveChangesAsync();
            });
            return (patient, reference);
        }

        private static async Task<Patient> DocumentPatientAsync(MobileEmergencyHost host, string nationalId)
        {
            var patient = Patient.Register("Luis", "Gómez", new DateOnly(1990, 1, 1), BiologicalSex.Male);
            patient.AddIdentifier(PatientIdentifierType.NationalId, nationalId, PatientIdentifierSource.Manual, isPrimary: true);
            await host.Web.DbAsync(async db =>
            {
                db.Patients.Add(patient);
                await db.SaveChangesAsync();
            });
            return patient;
        }

        private static Task<HttpResponseMessage> IdentifyByDocumentAsync(StaffSession paramedic, Guid emergencyId, string nationalId) =>
            paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergencyId}/identify/document",
                new { clientRequestId = Guid.NewGuid(), documentType = "NationalId", documentNumber = nationalId });

        private static Task<ClinicalEncounter> EncounterAsync(MobileEmergencyHost host, Guid id) =>
            host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == id));

        private static Task<FingerprintSession> StoredAsync(MobileEmergencyHost host, Guid sessionId) =>
            host.Web.DbAsync(async db => (await db.FingerprintIdentificationSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).ToSession());

        private static Task<List<IdentificationAttempt>> AttemptsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.IdentificationAttempts.AsNoTracking().Where(a => a.ClinicalEncounterId == emergencyId).ToListAsync());

        private static Task<int> IdentifyAuditsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergencyId && a.Action == AuditActionType.Identify));

        // Moves the session's window into the past (as if 90 s had gone by).
        private static Task ExpireAsync(MobileEmergencyHost host, Guid sessionId) =>
            host.Web.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE clinical."FingerprintIdentificationSessions" SET "CreatedAtUtc" = now() - interval '3 minutes', "ExpiresAtUtc" = now() - interval '1 minute', "ResolvedAtUtc" = CASE WHEN "ResolvedAtUtc" IS NULL THEN NULL ELSE now() - interval '2 minutes' END WHERE "Id" = {sessionId}"""));

        private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
            return problem.RootElement.Clone();
        }

        private sealed record Setup(MobileEmergencyHost Host, Clinic Base, StaffSession Paramedic, TestReader Reader, HttpClient ReaderClient);

        private async Task<Setup> SetupAsync()
        {
            var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var reader = await host.ProvisionReaderAsync();
            return new Setup(host, baseClinic, paramedic, reader, host.Reader(reader));
        }

        // The whole flow: the reader gets only a session id and an expiry, the paramedic sees the candidate, the emergency
        // stays NN until the confirmation, which identifies it once with the paramedic as the one who identified it.
        [SkippableFact]
        public async Task AMatchIsConfirmedByTheParamedicAndOnlyThenIdentifiesTheEmergency()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var (patient, reference) = await EnrolledPatientAsync(host, reader);
            var emergency = await OpenEmergencyAsync(paramedic);

            var readers = await (await paramedic.Client.GetAsync(Readers)).ReadAsAsync<JsonElement>();
            var listed = Assert.Single(readers.EnumerateArray());
            Assert.Equal(["id", "code", "name", "lastSeenAtUtc"], listed.EnumerateObject().Select(p => p.Name));
            Assert.Equal((reader.Id, reader.Code), (listed.GetProperty("id").GetGuid(), listed.GetProperty("code").GetString()));

            Assert.Equal(HttpStatusCode.NoContent, (await readerClient.GetAsync(Pending)).StatusCode);
            var key = Guid.NewGuid();
            var openResponse = await OpenAsync(paramedic, emergency.Id, reader.Id, key);
            Assert.Equal(HttpStatusCode.Created, openResponse.StatusCode);
            var opened = await JsonAsync(openResponse);
            var sessionId = Id(opened);
            Assert.Equal(("Pending", reader.Id), (opened.GetProperty("status").GetString(), opened.GetProperty("readerId").GetGuid()));
            var created = await StoredAsync(host, sessionId);
            Assert.Equal(TimeSpan.FromSeconds(90), created.ExpiresAtUtc - created.CreatedAtUtc);

            var pendingResponse = await readerClient.GetAsync(Pending);
            Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
            var pendingRaw = await pendingResponse.Content.ReadAsStringAsync();
            var pending = JsonDocument.Parse(pendingRaw).RootElement;
            Assert.Equal(["sessionId", "expiresAtUtc"], pending.EnumerateObject().Select(p => p.Name));
            Assert.Equal(sessionId, pending.GetProperty("sessionId").GetGuid());

            var result = await SubmitAsync(readerClient, sessionId, "Match", Slot);
            Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
            Assert.Empty(await result.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NoContent, (await readerClient.GetAsync(Pending)).StatusCode);

            var matchedResponse = await paramedic.Client.GetAsync($"{Sessions(emergency.Id)}/{sessionId}");
            var matchedRaw = await matchedResponse.Content.ReadAsStringAsync();
            var matched = JsonDocument.Parse(matchedRaw).RootElement;
            Assert.Equal("Matched", matched.GetProperty("status").GetString());
            var candidate = matched.GetProperty("candidate");
            Assert.Equal(["displayName", "dateOfBirth"], candidate.EnumerateObject().Select(p => p.Name));
            Assert.Equal(("Ana Pérez", "1985-03-14"), (candidate.GetProperty("displayName").GetString(), candidate.GetProperty("dateOfBirth").GetString()));
            Assert.DoesNotContain(patient.Id.ToString(), matchedRaw);
            Assert.DoesNotContain(reference.SubjectReference, matchedRaw);
            Assert.Null((await EncounterAsync(host, emergency.Id)).PatientId);

            var confirmKey = Guid.NewGuid();
            var confirm = await ConfirmAsync(paramedic, emergency.Id, sessionId, confirmKey);
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            var identified = await confirm.ReadAsAsync<PrehospitalEmergencySummary>();
            Assert.Equal((emergency.Id, emergency.TemporaryPatientCode, patient.Id), (identified.Id, identified.TemporaryPatientCode, identified.Patient!.Id));

            var encounter = await EncounterAsync(host, emergency.Id);
            Assert.Equal((patient.Id, paramedic.StaffId), (encounter.PatientId!.Value, encounter.IdentifiedByStaffId));
            Assert.Equal(emergency.TemporaryPatientCode, encounter.TemporaryPatientCode!.Value);
            var stored = await StoredAsync(host, sessionId);
            Assert.Equal((FingerprintSessionStatus.Confirmed, (Guid?)reference.Id, paramedic.StaffId!.Value),
                (stored.Status, stored.MatchedBiometricReferenceId, stored.RequestedByStaffId));
            var attempt = Assert.Single(await AttemptsAsync(host, emergency.Id));
            Assert.Equal((IdentificationMethod.Fingerprint, IdentificationOutcome.Success, (Guid?)patient.Id, (Guid?)reference.Id),
                (attempt.Method, attempt.Outcome, attempt.PatientId, attempt.BiometricReferenceId));
            Assert.Equal((reader.Code, sessionId.ToString("D"), "R307", paramedic.StaffId),
                (attempt.DeviceReference, attempt.ProviderRequestReference, attempt.Provider, attempt.PerformedByStaffId));
            var audits = await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking().Where(a => a.ClinicalEncounterId == emergency.Id).ToListAsync());
            var identify = Assert.Single(audits, a => a.Action == AuditActionType.Identify);
            Assert.Equal((patient.Id, paramedic.UserId, paramedic.StaffId), (identify.PatientId, identify.ActorIdentityUserId, identify.ActorStaffId));
            Assert.Single(audits, a => a.EntityName == EmergencyFingerprintService.SessionEntityName && a.Action == AuditActionType.Create);
            Assert.All(audits, a => Assert.DoesNotContain("r307", a.ChangesJson ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            // Retries: the lost confirmation and the reader's lost 204 change nothing.
            var replay = await ConfirmAsync(paramedic, emergency.Id, sessionId, confirmKey);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(identified, await replay.ReadAsAsync<PrehospitalEmergencySummary>());
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, sessionId, "Match", Slot)).StatusCode);
            Assert.Single(await AttemptsAsync(host, emergency.Id));
            Assert.Equal(1, await IdentifyAuditsAsync(host, emergency.Id));
            Assert.Equal(1, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == EmergencyFingerprintService.ConfirmOperation)));
            Assert.Equal(1, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == EmergencyFingerprintService.OpenSessionOperation)));

            // Nothing the reader received names the patient or the emergency.
            foreach (var secret in new[] { patient.Id.ToString(), "Ana", "Pérez", emergency.Id.ToString(), emergency.TemporaryPatientCode!, reference.SubjectReference })
            {
                Assert.DoesNotContain(secret, pendingRaw, StringComparison.OrdinalIgnoreCase);
            }
        }

        [SkippableFact]
        public async Task OnlyAnAuthenticatedReaderWithTheFingerprintScopeReachesTheSessions()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var emergency = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            var heartbeatOnly = await host.ProvisionReaderAsync(DeviceScopes.Heartbeat);
            var revoked = await host.ProvisionReaderAsync();
            await host.RevokeReaderAsync(revoked.Id);

            foreach (var client in new[]
            {
                host.DeviceAnonymous(),
                host.Reader(reader.Code, "wrong-key"),
                host.Reader("UNKNOWN-READER", reader.Key),
                host.Reader(revoked),
                host.DeviceAnonymous().WithBearer(paramedic.Client.DefaultRequestHeaders.Authorization!.Parameter!)
            })
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Pending)).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await SubmitAsync(client, sessionId, "NoMatch")).StatusCode);
            }

            Assert.Equal(HttpStatusCode.Forbidden, (await host.Reader(heartbeatOnly).GetAsync(Pending)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SubmitAsync(host.Reader(heartbeatOnly), sessionId, "NoMatch")).StatusCode);
            Assert.Equal(FingerprintSessionStatus.Pending, (await StoredAsync(host, sessionId)).Status);
            Assert.Equal(HttpStatusCode.OK, (await readerClient.GetAsync(Pending)).StatusCode);
        }

        // A reader only sees and answers its own sessions; the reader is never taken from the body.
        [SkippableFact]
        public async Task AReaderCannotSeeOrAnswerAnotherReadersSessionAndTheBodyIsStrict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var other = await host.ProvisionReaderAsync();
            var emergency = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));

            Assert.Equal(HttpStatusCode.NoContent, (await host.Reader(other).GetAsync(Pending)).StatusCode);
            var foreign = await AssertProblemAsync(await SubmitAsync(host.Reader(other), sessionId, "Match", Slot), HttpStatusCode.NotFound);
            var missing = await AssertProblemAsync(await SubmitAsync(readerClient, Guid.NewGuid(), "Match", Slot), HttpStatusCode.NotFound);
            Assert.Equal(foreign.GetProperty("detail").GetString(), missing.GetProperty("detail").GetString());

            foreach (var json in new[]
            {
                """{"outcome":"Match"}""",
                """{"outcome":"Match","slot":-1}""",
                """{"outcome":"NoMatch","slot":1}""",
                """{"outcome":"Other"}""",
                """{"outcome":1}""",
                """{"slot":1}""",
                $$"""{"outcome":"Match","slot":1,"deviceId":"{{reader.Id}}"}""",
                """{"outcome":"Match","slot":1,"template":"AAAA"}""",
                """{"outcome":"Match","slot":1,"patientId":"00000000-0000-0000-0000-000000000001"}"""
            })
            {
                await AssertProblemAsync(await PostJsonAsync(readerClient, Result(sessionId), json), HttpStatusCode.BadRequest);
            }

            Assert.Equal(FingerprintSessionStatus.Pending, (await StoredAsync(host, sessionId)).Status);
            Assert.Empty(await AttemptsAsync(host, emergency.Id));
        }

        // No usable candidate: the attempt fails, the emergency stays NN and the paramedic sees why.
        [SkippableTheory]
        [InlineData("NoMatch", null, "NoMatch", null, IdentificationFailureReason.NoMatch)]
        [InlineData("Match", 42, "NoMatch", null, IdentificationFailureReason.NoMatch)]
        [InlineData("LowQuality", null, "Failed", "LowQuality", IdentificationFailureReason.LowQualityCapture)]
        [InlineData("SensorError", null, "Failed", "SensorError", IdentificationFailureReason.ProviderUnavailable)]
        public async Task ResultsWithoutACandidateKeepTheEmergencyNn(
            string outcome, int? slot, string status, string? failure, IdentificationFailureReason reason)
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            await EnrolledPatientAsync(host, reader);
            var emergency = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));

            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, sessionId, outcome, slot)).StatusCode);

            var session = await SessionAsync(paramedic, emergency.Id, sessionId);
            Assert.Equal(status, session.GetProperty("status").GetString());
            Assert.Equal(failure, session.GetProperty("failure").GetString());
            Assert.Equal(JsonValueKind.Null, session.GetProperty("candidate").ValueKind);
            var attempt = Assert.Single(await AttemptsAsync(host, emergency.Id));
            Assert.Equal((IdentificationOutcome.Failure, (IdentificationFailureReason?)reason, (Guid?)null, (Guid?)null),
                (attempt.Outcome, attempt.FailureReason, attempt.PatientId, attempt.BiometricReferenceId));
            await AssertProblemAsync(await ConfirmAsync(paramedic, emergency.Id, sessionId), HttpStatusCode.Conflict);
            Assert.Null((await EncounterAsync(host, emergency.Id)).PatientId);
        }

        // A result is accepted once: the same result again is a 204 without a second attempt, another one a 409.
        [SkippableFact]
        public async Task AnIdenticalResultIsReplayedAndAChangedOneIsRejected()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            await EnrolledPatientAsync(host, reader);
            var emergency = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, sessionId, "Match", Slot)).StatusCode);
            var resolved = await StoredAsync(host, sessionId);

            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, sessionId, "Match", Slot)).StatusCode);
            foreach (var (outcome, slot) in new (string, int?)[] { ("Match", Slot + 1), ("NoMatch", null), ("LowQuality", null), ("SensorError", null) })
            {
                await AssertProblemAsync(await SubmitAsync(readerClient, sessionId, outcome, slot), HttpStatusCode.Conflict);
            }

            Assert.Equal(resolved, await StoredAsync(host, sessionId));
            Assert.Single(await AttemptsAsync(host, emergency.Id));
        }

        // Two different results at the same time: exactly one is accepted, with one attempt.
        [SkippableFact]
        public async Task ConcurrentResultsAcceptExactlyOne()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            await EnrolledPatientAsync(host, reader);

            for (var round = 0; round < 3; round++)
            {
                var emergency = await OpenEmergencyAsync(paramedic);
                var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));

                var responses = await Task.WhenAll(
                    SubmitAsync(host.Reader(reader), sessionId, "Match", Slot),
                    SubmitAsync(host.Reader(reader), sessionId, "NoMatch"));

                Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
                Assert.Single(await AttemptsAsync(host, emergency.Id));
                var stored = await StoredAsync(host, sessionId);
                Assert.Contains(stored.Status, new[] { FingerprintSessionStatus.Matched, FingerprintSessionStatus.NoMatch });
                await CancelAsync(paramedic, emergency.Id, sessionId);
            }
        }

        // A revoked reference is no longer a candidate, and a late result after 90 s is rejected.
        [SkippableFact]
        public async Task ARevokedReferenceOrAnExpiredSessionNeverIdentifies()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var (_, reference) = await EnrolledPatientAsync(host, reader);
            var emergency = await OpenEmergencyAsync(paramedic);
            var matchedId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, matchedId, "Match", Slot)).StatusCode);

            await host.Web.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE clinical."PatientBiometricReferences" SET "Status" = 2, "RevokedAtUtc" = GREATEST(now(), "CreatedAtUtc") WHERE "Id" = {reference.Id}"""));
            var revoked = await SessionAsync(paramedic, emergency.Id, matchedId);
            Assert.Equal(("Matched", JsonValueKind.Null), (revoked.GetProperty("status").GetString(), revoked.GetProperty("candidate").ValueKind));
            await AssertProblemAsync(await ConfirmAsync(paramedic, emergency.Id, matchedId), HttpStatusCode.Conflict);
            Assert.Null((await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(paramedic, emergency.Id, matchedId)).StatusCode);

            var lateId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            await ExpireAsync(host, lateId);
            Assert.Equal(HttpStatusCode.NoContent, (await readerClient.GetAsync(Pending)).StatusCode);
            await AssertProblemAsync(await SubmitAsync(readerClient, lateId, "Match", Slot), HttpStatusCode.Conflict);
            Assert.Equal("Expired", (await SessionAsync(paramedic, emergency.Id, lateId)).GetProperty("status").GetString());
            Assert.Equal(FingerprintSessionStatus.Expired, (await StoredAsync(host, lateId)).Status);
            Assert.Single(await AttemptsAsync(host, emergency.Id));

            // The expired session no longer blocks the emergency or the reader.
            Assert.Equal(HttpStatusCode.Created, (await OpenAsync(paramedic, emergency.Id, reader.Id)).StatusCode);
        }

        [SkippableFact]
        public async Task TheReadersListAndTheSessionsNeedTheParamedicsPermissions()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, baseClinic, paramedic, reader, _) = await SetupAsync();
            await using var _host = host;
            var revoked = await host.ProvisionReaderAsync();
            await host.RevokeReaderAsync(revoked.Id);
            var heartbeatOnly = await host.ProvisionReaderAsync(DeviceScopes.Heartbeat);
            var emergency = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));

            var listed = await (await paramedic.Client.GetAsync(Readers)).ReadAsAsync<JsonElement>();
            Assert.Equal([reader.Id], listed.EnumerateArray().Select(r => r.GetProperty("id").GetGuid()));
            var raw = await (await paramedic.Client.GetAsync(Readers)).Content.ReadAsStringAsync();
            Assert.DoesNotContain(reader.Key, raw);
            Assert.DoesNotContain("hash", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("scope", raw, StringComparison.OrdinalIgnoreCase);

            var anonymous = host.Anonymous();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Readers)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Sessions(emergency.Id), new { clientRequestId = Guid.NewGuid(), deviceId = reader.Id })).StatusCode);

            // A doctor has Patients.Identify but not Emergencies.Operate; a nurse neither.
            foreach (var (role, type) in new[] { (SecurityRoles.Doctor, ClinicalStaffType.Physician), (SecurityRoles.Nurse, ClinicalStaffType.Nurse) })
            {
                var other = await host.SignInAsync(role, [baseClinic], staffType: type);
                Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.GetAsync(Readers)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await OpenAsync(other, emergency.Id, reader.Id)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.GetAsync($"{Sessions(emergency.Id)}/{sessionId}")).StatusCode);
            }

            // Another paramedic is not assigned to this emergency.
            var otherParamedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            await AssertProblemAsync(await OpenAsync(otherParamedic, emergency.Id, reader.Id), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await otherParamedic.Client.GetAsync($"{Sessions(emergency.Id)}/{sessionId}"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await ConfirmAsync(otherParamedic, emergency.Id, sessionId), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await CancelAsync(otherParamedic, emergency.Id, sessionId), HttpStatusCode.Forbidden);

            // Its own emergency, but this session is not its own: not found.
            var otherEmergency = await OpenEmergencyAsync(otherParamedic);
            await AssertProblemAsync(await otherParamedic.Client.GetAsync($"{Sessions(otherEmergency.Id)}/{sessionId}"), HttpStatusCode.NotFound);
            await AssertProblemAsync(await paramedic.Client.GetAsync($"{Sessions(emergency.Id)}/{Guid.NewGuid()}"), HttpStatusCode.NotFound);

            // Readers that cannot be used, and an encounter that is not an emergency.
            await CancelAsync(paramedic, emergency.Id, sessionId);
            foreach (var readerId in new[] { Guid.NewGuid(), revoked.Id, heartbeatOnly.Id })
            {
                await AssertProblemAsync(await OpenAsync(paramedic, emergency.Id, readerId), HttpStatusCode.NotFound);
            }

            var clinicEncounter = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, DateTimeOffset.UtcNow.AddMinutes(-1), baseClinic.Id);
            await host.Web.DbAsync(async db =>
            {
                db.ClinicalEncounters.Add(clinicEncounter);
                await db.SaveChangesAsync();
            });
            await AssertProblemAsync(await OpenAsync(paramedic, clinicEncounter.Id, reader.Id), HttpStatusCode.NotFound);
            await AssertProblemAsync(await OpenAsync(paramedic, Guid.NewGuid(), reader.Id), HttpStatusCode.NotFound);
        }

        [SkippableFact]
        public async Task OpeningASessionIsIdempotentAndOneSessionIsActivePerEmergencyAndPerReader()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, _) = await SetupAsync();
            await using var _host = host;
            var other = await host.ProvisionReaderAsync();
            var emergency = await OpenEmergencyAsync(paramedic);
            var key = Guid.NewGuid();

            var first = await OpenAsync(paramedic, emergency.Id, reader.Id, key);
            var replay = await OpenAsync(paramedic, emergency.Id, reader.Id, key);
            Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.StatusCode, replay.StatusCode));
            Assert.Equal(Id(await JsonAsync(first)), Id(await JsonAsync(replay)));
            await AssertProblemAsync(await OpenAsync(paramedic, emergency.Id, other.Id, key), HttpStatusCode.Conflict);
            await AssertProblemAsync(await OpenAsync(paramedic, emergency.Id, other.Id), HttpStatusCode.Conflict);
            var secondEmergency = await OpenEmergencyAsync(paramedic);
            await AssertProblemAsync(await OpenAsync(paramedic, secondEmergency.Id, reader.Id), HttpStatusCode.Conflict);
            foreach (var json in new[]
            {
                $$"""{"clientRequestId":"{{Guid.NewGuid()}}"}""",
                $$"""{"deviceId":"{{other.Id}}"}""",
                $$"""{"clientRequestId":"{{Guid.Empty}}","deviceId":"{{other.Id}}"}""",
                $$"""{"clientRequestId":"{{Guid.NewGuid()}}","deviceId":"{{other.Id}}","patientId":"{{Guid.NewGuid()}}"}"""
            })
            {
                await AssertProblemAsync(await PostJsonAsync(paramedic.Client, Sessions(secondEmergency.Id), json), HttpStatusCode.BadRequest);
            }

            Assert.Equal(1, await host.Web.DbAsync(db => db.FingerprintIdentificationSessions.CountAsync()));

            // Concurrent opens: the same key gives one session; different keys, one 201 and one 409.
            var key2 = Guid.NewGuid();
            var sameKey = await Task.WhenAll(OpenAsync(paramedic, secondEmergency.Id, other.Id, key2), OpenAsync(paramedic, secondEmergency.Id, other.Id, key2));
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created], sameKey.Select(r => r.StatusCode).Order());
            var third = await OpenEmergencyAsync(paramedic);
            var thirdReader = await host.ProvisionReaderAsync();
            var fourthReader = await host.ProvisionReaderAsync();
            var differentKeys = await Task.WhenAll(OpenAsync(paramedic, third.Id, thirdReader.Id), OpenAsync(paramedic, third.Id, fourthReader.Id));
            Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], differentKeys.Select(r => r.StatusCode).Order());
            Assert.Equal(3, await host.Web.DbAsync(db => db.FingerprintIdentificationSessions.CountAsync()));
        }

        [SkippableFact]
        public async Task AnIdentifiedEmergencyCannotOpenOrConfirmAFingerprintSession()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            await EnrolledPatientAsync(host, reader);
            await DocumentPatientAsync(host, "001-7654321-0");
            var emergency = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, sessionId, "Match", Slot)).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await IdentifyByDocumentAsync(paramedic, emergency.Id, "00176543210")).StatusCode);

            await AssertProblemAsync(await ConfirmAsync(paramedic, emergency.Id, sessionId), HttpStatusCode.Conflict);
            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(paramedic, emergency.Id, sessionId)).StatusCode);
            await AssertProblemAsync(await OpenAsync(paramedic, emergency.Id, reader.Id), HttpStatusCode.Conflict);
            Assert.Equal(1, await IdentifyAuditsAsync(host, emergency.Id));
        }

        [SkippableFact]
        public async Task CancellingEndsTheSessionOnceAndAConfirmedSessionCannotBeCancelled()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            await EnrolledPatientAsync(host, reader);
            var emergency = await OpenEmergencyAsync(paramedic);

            var pendingId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            var cancelled = await CancelAsync(paramedic, emergency.Id, pendingId);
            var again = await CancelAsync(paramedic, emergency.Id, pendingId);
            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (cancelled.StatusCode, again.StatusCode));
            Assert.Equal("Cancelled", (await JsonAsync(again)).GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.NoContent, (await readerClient.GetAsync(Pending)).StatusCode);
            await AssertProblemAsync(await SubmitAsync(readerClient, pendingId, "Match", Slot), HttpStatusCode.Conflict);
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.EntityName == EmergencyFingerprintService.SessionEntityName && a.Action == AuditActionType.Update)));

            var matchedId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, matchedId, "Match", Slot)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(paramedic, emergency.Id, matchedId)).StatusCode);
            await AssertProblemAsync(await ConfirmAsync(paramedic, emergency.Id, matchedId), HttpStatusCode.Conflict);

            var expiredId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            await ExpireAsync(host, expiredId);
            var expired = await CancelAsync(paramedic, emergency.Id, expiredId);
            Assert.Equal((HttpStatusCode.OK, "Expired"), (expired.StatusCode, (await JsonAsync(expired)).GetProperty("status").GetString()));

            var confirmedId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, confirmedId, "Match", Slot)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(paramedic, emergency.Id, confirmedId)).StatusCode);
            await AssertProblemAsync(await CancelAsync(paramedic, emergency.Id, confirmedId), HttpStatusCode.Conflict);
            Assert.Equal(2, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.EntityName == EmergencyFingerprintService.SessionEntityName && a.Action == AuditActionType.Update)));
        }

        // The mobile identification (document and fingerprint) works on the way (Transferred without clinic) and ends at the
        // hospital's reception, also for a paramedic affiliated with the receiving clinic. The hospital is unaffected.
        [SkippableTheory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TheMobileIdentificationWorksOnTheWayAndEndsAtTheHandoff(bool paramedicAffiliatedWithDestination)
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = paramedicAffiliatedWithDestination ? baseClinic : await host.Web.CreateClinicAsync("Hospital receptor");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var doctor = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospital], staffType: ClinicalStaffType.Physician);
            var reader = await host.ProvisionReaderAsync();
            var readerClient = host.Reader(reader);
            var (patient, _) = await EnrolledPatientAsync(host, reader);
            var documentPatient = await DocumentPatientAsync(host, "001-1111111-1");

            // On the way: identified by fingerprint while Transferred without clinic.
            var onTheWay = await OpenEmergencyAsync(paramedic);
            await StartTransportAsync(paramedic, onTheWay.Id, hospital.Id);
            Assert.Equal((ClinicalEncounterStatus.Transferred, (Guid?)null), await StatusAsync(host, onTheWay.Id));
            var sessionId = Id(await OpenedAsync(paramedic, onTheWay.Id, reader.Id));
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, sessionId, "Match", Slot)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(paramedic, onTheWay.Id, sessionId)).StatusCode);
            Assert.Equal(patient.Id, (await EncounterAsync(host, onTheWay.Id)).PatientId);

            // On the way: identified by document while Transferred without clinic.
            var byDocument = await OpenEmergencyAsync(paramedic);
            await StartTransportAsync(paramedic, byDocument.Id, hospital.Id);
            Assert.Equal(HttpStatusCode.OK, (await IdentifyByDocumentAsync(paramedic, byDocument.Id, "00111111111")).StatusCode);

            // Received by the hospital while still NN, with a matched session waiting.
            var received = await OpenEmergencyAsync(paramedic);
            var transferId = await StartTransportAsync(paramedic, received.Id, hospital.Id);
            var waitingId = Id(await OpenedAsync(paramedic, received.Id, reader.Id));
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, waitingId, "Match", Slot)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await doctor.Client.PostAsync($"/api/web/transfers/{transferId}/receive", null)).StatusCode);
            Assert.Equal((ClinicalEncounterStatus.Received, (Guid?)hospital.Id), await StatusAsync(host, received.Id));

            await AssertProblemAsync(await IdentifyByDocumentAsync(paramedic, received.Id, "00111111111"), HttpStatusCode.Conflict);
            await AssertProblemAsync(await ConfirmAsync(paramedic, received.Id, waitingId), HttpStatusCode.Conflict);
            await AssertProblemAsync(await CancelAsync(paramedic, received.Id, waitingId), HttpStatusCode.Conflict);
            await AssertProblemAsync(await OpenAsync(paramedic, received.Id, reader.Id), HttpStatusCode.Conflict);
            Assert.Null((await EncounterAsync(host, received.Id)).PatientId);

            // The hospital identifies through the Web, unchanged.
            Assert.Equal(HttpStatusCode.NoContent, (await doctor.Client.PostAsJsonAsync(
                $"/api/web/encounters/{received.Id}/identify", new { patientId = documentPatient.Id })).StatusCode);
            Assert.Equal(documentPatient.Id, (await EncounterAsync(host, received.Id)).PatientId);
        }

        // Fingerprint vs document: the document identification commits while the confirmation runs; the confirmation
        // loses on the PatientId token (409) and never overwrites the patient.
        [SkippableFact]
        public async Task AConfirmationLosingAgainstAnotherIdentificationIsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            await EnrolledPatientAsync(host, reader);
            var documentPatient = await DocumentPatientAsync(host, "001-2222222-2");
            var emergency = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, sessionId, "Match", Slot)).StatusCode);

            var (connection, identify) = await ClinicalRace.BeginIdentifyAsync(host.ConnectionString, emergency.Id, documentPatient.Id, paramedic.StaffId!.Value);
            await using (connection)
            {
                var confirm = ConfirmAsync(paramedic, emergency.Id, sessionId);
                await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, confirm);
                await identify.CommitAsync();
                await AssertProblemAsync(await confirm, HttpStatusCode.Conflict);
            }

            Assert.Equal(documentPatient.Id, (await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Equal(FingerprintSessionStatus.Matched, (await StoredAsync(host, sessionId)).Status);
            Assert.Equal(0, await IdentifyAuditsAsync(host, emergency.Id));
            Assert.Equal(0, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == EmergencyFingerprintService.ConfirmOperation)));
        }

        // Fingerprint vs reception: the hospital receives while the confirmation runs; the confirmation loses (409).
        [SkippableFact]
        public async Task AConfirmationLosingAgainstTheReceptionIsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, baseClinic, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var hospital = await host.Web.CreateClinicAsync("Hospital");
            await EnrolledPatientAsync(host, reader);
            var emergency = await OpenEmergencyAsync(paramedic);
            var transferId = await StartTransportAsync(paramedic, emergency.Id, hospital.Id);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, sessionId, "Match", Slot)).StatusCode);

            var (connection, receive) = await ClinicalRace.BeginReceiveAsync(host.ConnectionString, transferId, emergency.Id, hospital.Id, paramedic.StaffId!.Value);
            await using (connection)
            {
                var confirm = ConfirmAsync(paramedic, emergency.Id, sessionId);
                await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, confirm);
                await receive.CommitAsync();
                await AssertProblemAsync(await confirm, HttpStatusCode.Conflict);
            }

            await AssertProblemAsync(await ConfirmAsync(paramedic, emergency.Id, sessionId), HttpStatusCode.Conflict);
            Assert.Null((await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Equal(FingerprintSessionStatus.Matched, (await StoredAsync(host, sessionId)).Status);
        }

        // Two confirmations of the same session with different keys: one identification and one audit entry.
        [SkippableFact]
        public async Task ConcurrentConfirmationsIdentifyOnce()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var (patient, _) = await EnrolledPatientAsync(host, reader);
            var emergency = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, sessionId, "Match", Slot)).StatusCode);

            var responses = await Task.WhenAll(ConfirmAsync(paramedic, emergency.Id, sessionId), ConfirmAsync(paramedic, emergency.Id, sessionId));

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
            Assert.Equal(patient.Id, (await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Equal(1, await IdentifyAuditsAsync(host, emergency.Id));
            Assert.Equal(FingerprintSessionStatus.Confirmed, (await StoredAsync(host, sessionId)).Status);
        }

        // A cancellation racing the reader's result: one of them wins, never both, never a 500.
        [SkippableFact]
        public async Task ACancellationRacingTheResultEndsInOneConsistentState()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            await EnrolledPatientAsync(host, reader);

            for (var round = 0; round < 3; round++)
            {
                var emergency = await OpenEmergencyAsync(paramedic);
                var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));

                var (result, cancel) = (SubmitAsync(host.Reader(reader), sessionId, "Match", Slot), CancelAsync(paramedic, emergency.Id, sessionId));
                await Task.WhenAll(result, cancel);

                var stored = await StoredAsync(host, sessionId);
                Assert.Equal(FingerprintSessionStatus.Cancelled, stored.Status);
                Assert.Equal(HttpStatusCode.OK, (await cancel).StatusCode);
                var attempts = await AttemptsAsync(host, emergency.Id);
                Assert.Equal((await result).StatusCode == HttpStatusCode.NoContent ? 1 : 0, attempts.Count);
                Assert.Contains((await result).StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict });
            }
        }

        // Nothing sensitive is written to the logs of the Mobile BFF or the Device API, nor returned in a ProblemDetails.
        [SkippableFact]
        public async Task NoKeySlotOrSubjectReferenceIsLoggedOrReturned()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var (patient, reference) = await EnrolledPatientAsync(host, reader, slot: 7319);
            var emergency = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            var bodies = new List<string>
            {
                await (await readerClient.GetAsync(Pending)).Content.ReadAsStringAsync(),
                await (await SubmitAsync(readerClient, sessionId, "Match", 7319)).Content.ReadAsStringAsync(),
                await (await SubmitAsync(readerClient, sessionId, "Match", 7320)).Content.ReadAsStringAsync(),
                await (await SubmitAsync(host.Reader(reader.Code, "wrong-key"), sessionId, "Match", 7319)).Content.ReadAsStringAsync(),
                await (await PostJsonAsync(readerClient, Result(sessionId), """{"outcome":"Match","slot":-7319}""")).Content.ReadAsStringAsync()
            };
            Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(paramedic, emergency.Id, sessionId)).StatusCode);

            var logs = host.Logs.Text;
            Assert.NotEmpty(logs);
            foreach (var secret in new[] { reader.Key, reference.SubjectReference, "r307:device", "7319" })
            {
                Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var body in bodies)
            {
                foreach (var secret in new[] { reader.Key, reference.SubjectReference, "r307", "7319", patient.Id.ToString(), "Pérez", emergency.Id.ToString() })
                {
                    Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
                }
            }

            Assert.DoesNotContain(patient.Id.ToString(), logs);
        }

        // The table's own rules hold at the database level, whatever the application does.
        [SkippableFact]
        public async Task TheDatabaseEnforcesOneActiveSessionPerEmergencyAndOnePendingPerReader()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, _) = await SetupAsync();
            await using var _host = host;
            var emergency = await OpenEmergencyAsync(paramedic);
            var other = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            var otherReader = Guid.NewGuid();

            async Task<string?> InsertAsync(Guid encounterId, Guid readerId, int status = 1)
            {
                try
                {
                    await host.Web.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
                        $"""INSERT INTO clinical."FingerprintIdentificationSessions" ("Id", "ClinicalEncounterId", "DeviceCredentialId", "RequestedByStaffId", "Status", "CreatedAtUtc", "ExpiresAtUtc") VALUES ({Guid.NewGuid()}, {encounterId}, {readerId}, {paramedic.StaffId!.Value}, {status}, now(), now() + interval '90 seconds')"""));
                    return null;
                }
                catch (PostgresException exception)
                {
                    return exception.ConstraintName;
                }
            }

            Assert.Equal("UX_FingerprintIdentificationSessions_ActivePerEncounter", await InsertAsync(emergency.Id, otherReader));
            Assert.Equal("UX_FingerprintIdentificationSessions_PendingPerReader", await InsertAsync(other.Id, reader.Id));
            Assert.Equal("CK_FingerprintIdentificationSessions_Lifecycle", await InsertAsync(other.Id, otherReader, status: 2));
            Assert.Null(await InsertAsync(other.Id, otherReader));
            Assert.Equal(FingerprintSessionStatus.Pending, (await StoredAsync(host, sessionId)).Status);
        }

        private static async Task<Guid> StartTransportAsync(StaffSession paramedic, Guid emergencyId, Guid destination)
        {
            var requested = await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergencyId}/transfers",
                new { clientRequestId = Guid.NewGuid(), destinationClinicId = destination });
            Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
            var transferId = (await JsonAsync(requested)).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await paramedic.Client.PostAsync($"{Emergencies}/{emergencyId}/transfers/{transferId}/start", null)).StatusCode);
            return transferId;
        }

        private static async Task<(ClinicalEncounterStatus, Guid?)> StatusAsync(MobileEmergencyHost host, Guid id)
        {
            var encounter = await EncounterAsync(host, id);
            return (encounter.Status, encounter.ClinicId);
        }

        // ---- The session's reader must still be a fingerprint reader when the paramedic confirms (ADR-009) ----

        private static Task<int> ConfirmReceiptsAsync(MobileEmergencyHost host) =>
            host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == EmergencyFingerprintService.ConfirmOperation));

        // CASE 2 (and CASE 1 on another emergency): the reader of a Matched session is revoked (committed) before the
        // confirmation. The candidate disappears, Confirm is 409 every time with the same key, and nothing is written: the
        // emergency stays NN, no Identify entry, no receipt, the session stays Matched and the reference Active. The
        // paramedic cancels it and can go on with another reader or a document.
        [SkippableFact]
        public async Task AMatchCannotBeConfirmedOnceItsReaderIsRevoked()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var (_, reference) = await EnrolledPatientAsync(host, reader);
            var emergency = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, sessionId, "Match", Slot)).StatusCode);
            Assert.Equal(JsonValueKind.Object, (await SessionAsync(paramedic, emergency.Id, sessionId)).GetProperty("candidate").ValueKind);

            await host.RevokeReaderAsync(reader.Id);

            var shown = await SessionAsync(paramedic, emergency.Id, sessionId);
            Assert.Equal(("Matched", JsonValueKind.Null), (shown.GetProperty("status").GetString(), shown.GetProperty("candidate").ValueKind));
            var key = Guid.NewGuid();
            var first = await ConfirmAsync(paramedic, emergency.Id, sessionId, key);
            var again = await ConfirmAsync(paramedic, emergency.Id, sessionId, key);
            Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (first.StatusCode, again.StatusCode));
            var detail = await first.Content.ReadAsStringAsync();
            Assert.DoesNotContain("revoked", detail, StringComparison.OrdinalIgnoreCase);
            Assert.Null((await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Equal(0, await IdentifyAuditsAsync(host, emergency.Id));
            Assert.Equal(0, await ConfirmReceiptsAsync(host));
            Assert.Equal(FingerprintSessionStatus.Matched, (await StoredAsync(host, sessionId)).Status);
            Assert.Equal(BiometricReferenceStatus.Active,
                (await host.Web.DbAsync(db => db.PatientBiometricReferences.AsNoTracking().SingleAsync(r => r.Id == reference.Id))).Status);

            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(paramedic, emergency.Id, sessionId)).StatusCode);
            var other = await host.ProvisionReaderAsync();
            Assert.Equal(HttpStatusCode.Created, (await OpenAsync(paramedic, emergency.Id, other.Id)).StatusCode);
            var document = await DocumentPatientAsync(host, "00176543210");
            Assert.Equal(HttpStatusCode.OK, (await IdentifyByDocumentAsync(paramedic, emergency.Id, "00176543210")).StatusCode);
            Assert.Equal(document.Id, (await EncounterAsync(host, emergency.Id)).PatientId);
        }

        // CASE 1 and CASE 3: with its reader active the match is confirmed, also when ANOTHER reader was revoked meanwhile.
        [SkippableFact]
        public async Task RevokingAnotherReaderDoesNotAffectTheConfirmation()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var (patient, _) = await EnrolledPatientAsync(host, reader);
            var bystander = await host.ProvisionReaderAsync();
            var emergency = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            await SubmitAsync(readerClient, sessionId, "Match", Slot);

            await host.RevokeReaderAsync(bystander.Id);

            Assert.Equal(JsonValueKind.Object, (await SessionAsync(paramedic, emergency.Id, sessionId)).GetProperty("candidate").ValueKind);
            Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(paramedic, emergency.Id, sessionId)).StatusCode);
            Assert.Equal(patient.Id, (await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Equal(1, await IdentifyAuditsAsync(host, emergency.Id));
        }

        // CASE 4 (cross-context order B): the confirmation committed before the revocation is a valid identification, and
        // its replay with the same key after the revocation returns the same success (never a retroactive error).
        [SkippableFact]
        public async Task AConfirmationCommittedBeforeTheRevocationReplaysAsTheSameSuccess()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, paramedic, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var (patient, _) = await EnrolledPatientAsync(host, reader);
            var emergency = await OpenEmergencyAsync(paramedic);
            var sessionId = Id(await OpenedAsync(paramedic, emergency.Id, reader.Id));
            await SubmitAsync(readerClient, sessionId, "Match", Slot);
            var key = Guid.NewGuid();
            var confirmed = await ConfirmAsync(paramedic, emergency.Id, sessionId, key);
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

            await host.RevokeReaderAsync(reader.Id);
            var replay = await ConfirmAsync(paramedic, emergency.Id, sessionId, key);

            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(await confirmed.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
            Assert.Equal(patient.Id, (await EncounterAsync(host, emergency.Id)).PatientId);
            Assert.Equal(1, await IdentifyAuditsAsync(host, emergency.Id));
            Assert.Equal(1, await ConfirmReceiptsAsync(host));
        }
    }
}
