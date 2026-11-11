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
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Fingerprint revocation over the real hosts and PostgreSQL (ADR-011): hospital staff (Web BFF) revoke a reference, which
    // identifies nobody from that commit on (Mobile BFF + Device API), and its reader (Device API) deletes the slot attempt by
    // attempt. One deletion job per reference, also under concurrent revocations; a confirmation loses against a revocation;
    // a slot waiting for its deletion cannot be enrolled again; nothing about the slot reaches the Web and nothing about the
    // patient reaches the reader.
    [Collection(PostgresCollection.Name)]
    public sealed class FingerprintRevocationApiTests
    {
        private const string PendingDeletion = "/api/device/fingerprint/deletions/pending";
        private const int Slot = 5;
        private readonly PostgresFixture _postgres;

        public FingerprintRevocationApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static string Fingerprints(Guid patientId) => $"/api/web/patients/{patientId}/fingerprints";

        private static string Revoke(Guid patientId, Guid referenceId) => $"{Fingerprints(patientId)}/{referenceId}/revoke";

        private static string DeletionResult(Guid deletionId) => $"/api/device/fingerprint/deletions/{deletionId}/result";

        private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, string json) =>
            client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

        private static Task<HttpResponseMessage> RevokeAsync(StaffSession staff, Guid patientId, Guid referenceId, string json = """{"reason":"PatientRequest"}""") =>
            PostJsonAsync(staff.Client, Revoke(patientId, referenceId), json);

        private static Task<HttpResponseMessage> ReportAsync(HttpClient reader, Guid deletionId, Guid attemptId, string outcome) =>
            PostJsonAsync(reader, DeletionResult(deletionId), $$"""{"attemptId":"{{attemptId}}","outcome":"{{outcome}}"}""");

        private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        private static Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            return Task.CompletedTask;
        }

        private static async Task<JsonElement> ListedAsync(StaffSession staff, Guid patientId, Guid referenceId)
        {
            var listed = await JsonAsync(await staff.Client.GetAsync(Fingerprints(patientId)));
            return listed.EnumerateArray().Single(f => f.GetProperty("id").GetGuid() == referenceId);
        }

        private static Task<PatientBiometricReference> ReferenceAsync(MobileEmergencyHost host, Guid referenceId) =>
            host.Web.DbAsync(db => db.PatientBiometricReferences.AsNoTracking().SingleAsync(r => r.Id == referenceId));

        private static Task<List<FingerprintSlotDeletion>> JobsAsync(MobileEmergencyHost host, Guid referenceId) =>
            host.Web.DbAsync(async db => (await db.FingerprintSlotDeletionJobs.AsNoTracking().Where(j => j.BiometricReferenceId == referenceId).ToListAsync())
                .Select(j => j.ToDeletion()).ToList());

        private static Task<int> AuditsAsync(MobileEmergencyHost host, string entityName, AuditActionType action) =>
            host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.EntityName == entityName && a.Action == action));

        private sealed record Setup(MobileEmergencyHost Host, Clinic Clinic, StaffSession Reception, Patient Patient, TestReader Reader, HttpClient ReaderClient);

        private async Task<Setup> SetupAsync()
        {
            var host = await MobileEmergencyHost.StartAsync(_postgres);
            var clinic = await host.Web.CreateClinicAsync();
            var reception = await host.Web.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var patient = await host.Web.SeedPatientAsync();
            var reader = await host.ProvisionReaderAsync(DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            return new Setup(host, clinic, reception, patient, reader, host.Reader(reader));
        }

        // Enrolls the patient on the reader in `slot` through the real endpoints and returns the new reference id.
        private static async Task<Guid> EnrollAsync(Setup s, Guid patientId, int slot = Slot, TestReader? reader = null)
        {
            var on = reader ?? s.Reader;
            var opened = await s.Reception.Client.PostAsJsonAsync($"{Fingerprints(patientId)}/enrollments", new { clientRequestId = Guid.NewGuid(), deviceId = on.Id });
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
            var enrollmentId = (await JsonAsync(opened)).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.NoContent, (await PostJsonAsync(s.Host.Reader(on),
                $"/api/device/fingerprint/enrollments/{enrollmentId}/result", $$"""{"outcome":"Enrolled","slot":{{slot}}}""")).StatusCode);
            return (await s.Host.Web.DbAsync(db => db.FingerprintEnrollmentSessions.AsNoTracking().SingleAsync(e => e.Id == enrollmentId))).BiometricReferenceId!.Value;
        }

        private static async Task<(StaffSession Paramedic, Guid EmergencyId, string Sessions)> EmergencyAsync(Setup s)
        {
            var paramedic = await s.Host.SignInAsync(SecurityRoles.Paramedic, [s.Clinic]);
            var emergency = await (await paramedic.Client.PostAsJsonAsync("/api/mobile/emergencies", new
            {
                clientRequestId = Guid.NewGuid(),
                startedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                chiefComplaint = "Inconsciente"
            })).ReadAsAsync<PrehospitalEmergencySummary>();
            return (paramedic, emergency.Id, $"/api/mobile/emergencies/{emergency.Id}/identify/fingerprint/sessions");
        }

        // The whole lifecycle: revoke (logical first, in one commit with its job), the slot still on the sensor matches but
        // identifies nobody, the slot cannot be enrolled meanwhile, the reader deletes it and only then it is free again.
        [SkippableFact]
        public async Task ARevokedFingerprintIdentifiesNobodyAndItsSlotIsFreedOnlyAfterTheReaderDeletesIt()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;
            var referenceId = await EnrollAsync(s, s.Patient.Id);
            Assert.Equal(JsonValueKind.Null, (await ListedAsync(s.Reception, s.Patient.Id, referenceId)).GetProperty("cleanup").ValueKind);

            var response = await RevokeAsync(s.Reception, s.Patient.Id, referenceId);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var raw = await response.Content.ReadAsStringAsync();
            var revoked = JsonDocument.Parse(raw).RootElement;
            Assert.Equal(["id", "status", "reader", "registeredAtUtc", "revokedAtUtc", "cleanup"], revoked.EnumerateObject().Select(p => p.Name));
            Assert.Equal(("Revoked", "Pending"), (revoked.GetProperty("status").GetString(), revoked.GetProperty("cleanup").GetString()));
            foreach (var secret in new[] { "r307", "slot", "subjectReference", s.Reader.Id.ToString() })
            {
                Assert.DoesNotContain(secret, raw, StringComparison.OrdinalIgnoreCase);
            }

            var reference = await ReferenceAsync(s.Host, referenceId);
            Assert.Equal(BiometricReferenceStatus.Revoked, reference.Status);
            var job = Assert.Single(await JobsAsync(s.Host, referenceId));
            Assert.Equal((s.Reader.Id, FingerprintSlotDeletionStatus.Pending, 0), (job.DeviceCredentialId, job.Status, job.Attempts));
            Assert.Equal(1, await AuditsAsync(s.Host, nameof(PatientBiometricReference), AuditActionType.Update));
            Assert.Equal(1, await AuditsAsync(s.Host, FingerprintRevocationService.DeletionEntityName, AuditActionType.Create));

            // Replay: the same answer, nothing written, same revocation time.
            var again = await RevokeAsync(s.Reception, s.Patient.Id, referenceId, """{"reason":"Other"}""");
            Assert.Equal((HttpStatusCode.OK, raw), (again.StatusCode, await again.Content.ReadAsStringAsync()));
            Assert.Single(await JobsAsync(s.Host, referenceId));
            Assert.Equal(1, await AuditsAsync(s.Host, nameof(PatientBiometricReference), AuditActionType.Update));
            Assert.Equal(reference.RevokedAtUtc, (await ReferenceAsync(s.Host, referenceId)).RevokedAtUtc);

            // The sensor still holds the template and matches its slot: NoMatch, no candidate, a generic 204 to the reader.
            var (paramedic, emergencyId, sessions) = await EmergencyAsync(s);
            var sessionId = (await JsonAsync(await paramedic.Client.PostAsJsonAsync(sessions, new { clientRequestId = Guid.NewGuid(), deviceId = s.Reader.Id })))
                .GetProperty("id").GetGuid();
            var match = await PostJsonAsync(s.ReaderClient, $"/api/device/fingerprint/sessions/{sessionId}/result", $$"""{"outcome":"Match","slot":{{Slot}}}""");
            Assert.Equal((HttpStatusCode.NoContent, string.Empty), (match.StatusCode, await match.Content.ReadAsStringAsync()));
            var session = await JsonAsync(await paramedic.Client.GetAsync($"{sessions}/{sessionId}"));
            Assert.Equal(("NoMatch", JsonValueKind.Null), (session.GetProperty("status").GetString(), session.GetProperty("candidate").ValueKind));
            var attempt = await s.Host.Web.DbAsync(db => db.IdentificationAttempts.AsNoTracking().SingleAsync(a => a.ProviderRequestReference == sessionId.ToString("D")));
            Assert.Equal((IdentificationFailureReason.NoMatch, (Guid?)null, (Guid?)null), (attempt.FailureReason, attempt.BiometricReferenceId, attempt.PatientId));
            Assert.Null((await s.Host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == emergencyId))).PatientId);

            // While the deletion is pending, another patient cannot get that slot (409, the enrollment stays Pending).
            var other = await s.Host.Web.SeedPatientAsync("Luis", "Gómez");
            var opened = await JsonAsync(await s.Reception.Client.PostAsJsonAsync($"{Fingerprints(other.Id)}/enrollments", new { clientRequestId = Guid.NewGuid(), deviceId = s.Reader.Id }));
            var enrollmentId = opened.GetProperty("id").GetGuid();
            var enrollResult = $"/api/device/fingerprint/enrollments/{enrollmentId}/result";
            await AssertProblemAsync(await PostJsonAsync(s.ReaderClient, enrollResult, $$"""{"outcome":"Enrolled","slot":{{Slot}}}"""), HttpStatusCode.Conflict);
            Assert.Equal(FingerprintEnrollmentStatus.Pending,
                (await s.Host.Web.DbAsync(db => db.FingerprintEnrollmentSessions.AsNoTracking().SingleAsync(e => e.Id == enrollmentId))).Status);

            // The reader deletes its copy: the slot (taken from the reference) and nothing about the patient.
            var pendingRaw = await (await s.ReaderClient.GetAsync(PendingDeletion)).Content.ReadAsStringAsync();
            var pending = JsonDocument.Parse(pendingRaw).RootElement;
            Assert.Equal(["deletionId", "slot"], pending.EnumerateObject().Select(p => p.Name));
            Assert.Equal((job.Id, Slot), (pending.GetProperty("deletionId").GetGuid(), pending.GetProperty("slot").GetInt32()));
            foreach (var secret in new[] { s.Patient.Id.ToString(), referenceId.ToString(), "Ana", "Pérez", "r307" })
            {
                Assert.DoesNotContain(secret, pendingRaw, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(HttpStatusCode.NoContent, (await ReportAsync(s.ReaderClient, job.Id, Guid.NewGuid(), "Deleted")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await s.ReaderClient.GetAsync(PendingDeletion)).StatusCode);
            Assert.Equal("Completed", (await ListedAsync(s.Reception, s.Patient.Id, referenceId)).GetProperty("cleanup").GetString());

            // Now the slot is free: the same enrollment takes it.
            Assert.Equal(HttpStatusCode.NoContent, (await PostJsonAsync(s.ReaderClient, enrollResult, $$"""{"outcome":"Enrolled","slot":{{Slot}}}""")).StatusCode);
            var reused = await s.Host.Web.DbAsync(db => db.PatientBiometricReferences.AsNoTracking()
                .SingleAsync(r => r.PatientId == other.Id && r.Status == BiometricReferenceStatus.Active));
            Assert.Equal(FingerprintReferences.SubjectReference(s.Reader.Id, Slot), reused.SubjectReference);
        }

        // Biometrics.Manage AND active clinical staff who can see the patient; the reference must be one of the patient's
        // fingerprints; the body is optional and strict.
        [SkippableFact]
        public async Task OnlyAuthorizedStaffRevokeThePatientsOwnFingerprintsWithAStrictBody()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;
            var referenceId = await EnrollAsync(s, s.Patient.Id);
            var other = await s.Host.Web.SeedPatientAsync("Luis", "Gómez");
            var face = PatientBiometricReference.Register(s.Patient.Id, BiometricModality.Face, "FaceProvider", "face-subject-1");
            await s.Host.Web.DbAsync(async db =>
            {
                db.PatientBiometricReferences.Add(face);
                await db.SaveChangesAsync();
            });

            Assert.Equal(HttpStatusCode.Unauthorized, (await PostJsonAsync(s.Host.Web.Anonymous(), Revoke(s.Patient.Id, referenceId), "{}")).StatusCode);
            foreach (var (role, type) in new[]
            {
                (SecurityRoles.Doctor, ClinicalStaffType.Physician), (SecurityRoles.Nurse, ClinicalStaffType.Nurse),
                (SecurityRoles.Paramedic, ClinicalStaffType.Paramedic)
            })
            {
                var staff = await s.Host.Web.SignInAsync(role, [s.Clinic], staffType: type);
                Assert.Equal(HttpStatusCode.Forbidden, (await RevokeAsync(staff, s.Patient.Id, referenceId)).StatusCode);
            }

            var adminWithoutStaff = await s.Host.Web.SignInAsync(SecurityRoles.Administrator, [], asClinicalStaff: false);
            await AssertProblemAsync(await RevokeAsync(adminWithoutStaff, s.Patient.Id, referenceId), HttpStatusCode.Forbidden);
            var inactiveReception = await s.Host.Web.SignInAsync(SecurityRoles.Receptionist, [s.Clinic], staffActive: false);
            await AssertProblemAsync(await RevokeAsync(inactiveReception, s.Patient.Id, referenceId), HttpStatusCode.Forbidden);

            await AssertProblemAsync(await RevokeAsync(s.Reception, other.Id, referenceId), HttpStatusCode.NotFound);
            await AssertProblemAsync(await RevokeAsync(s.Reception, s.Patient.Id, face.Id), HttpStatusCode.NotFound);
            await AssertProblemAsync(await RevokeAsync(s.Reception, s.Patient.Id, Guid.NewGuid()), HttpStatusCode.NotFound);
            await AssertProblemAsync(await RevokeAsync(s.Reception, Guid.NewGuid(), referenceId), HttpStatusCode.NotFound);
            foreach (var json in new[]
            {
                """{"reason":"Bogus"}""", """{"reason":1}""", """{"reason":""}""", $$"""{"slot":{{Slot}}}""",
                """{"subjectReference":"r307:device:x:slot:5"}""", $$"""{"deviceId":"{{s.Reader.Id}}"}""", """{"status":"Revoked"}""",
                """{"revokedAt":"2026-01-01T00:00:00Z"}""", $$"""{"staffId":"{{Guid.NewGuid()}}"}""", $$"""{"patientId":"{{s.Patient.Id}}"}"""
            })
            {
                await AssertProblemAsync(await RevokeAsync(s.Reception, s.Patient.Id, referenceId, json), HttpStatusCode.BadRequest);
            }

            Assert.Equal(BiometricReferenceStatus.Active, (await ReferenceAsync(s.Host, referenceId)).Status);
            Assert.Empty(await JobsAsync(s.Host, referenceId));

            // No body at all is a revocation without a reason; the Administrator with clinical staff may revoke too.
            var admin = await s.Host.Web.SignInAsync(SecurityRoles.Administrator, [s.Clinic]);
            var response = await admin.Client.PostAsync(Revoke(s.Patient.Id, referenceId), null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(BiometricReferenceStatus.Revoked, (await ReferenceAsync(s.Host, referenceId)).Status);
            var audit = await s.Host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .SingleAsync(a => a.EntityName == nameof(PatientBiometricReference) && a.Action == AuditActionType.Update));
            Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(audit.ChangesJson!).RootElement.GetProperty("reason").ValueKind);
        }

        // attemptId per PHYSICAL attempt: a resent attempt never counts twice, a new one does; only the first sensor error is
        // audited; Deleted completes the job and its outcome never changes. Strict body; another reader's job is not found.
        [SkippableFact]
        public async Task TheReaderReportsEachAttemptOnceAndACompletedDeletionNeverChanges()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;
            var referenceId = await EnrollAsync(s, s.Patient.Id);
            await RevokeAsync(s.Reception, s.Patient.Id, referenceId);
            var id = Assert.Single(await JobsAsync(s.Host, referenceId)).Id;
            var otherEnroller = await s.Host.ProvisionReaderAsync(DeviceScopes.FingerprintEnroll);
            var identifyOnly = await s.Host.ProvisionReaderAsync(DeviceScopes.FingerprintIdentify);
            var revokedReader = await s.Host.ProvisionReaderAsync(DeviceScopes.FingerprintEnroll);
            await s.Host.RevokeReaderAsync(revokedReader.Id);

            Assert.Equal(HttpStatusCode.NoContent, (await s.Host.Reader(otherEnroller).GetAsync(PendingDeletion)).StatusCode);
            await AssertProblemAsync(await ReportAsync(s.Host.Reader(otherEnroller), id, Guid.NewGuid(), "Deleted"), HttpStatusCode.NotFound);
            await AssertProblemAsync(await ReportAsync(s.ReaderClient, Guid.NewGuid(), Guid.NewGuid(), "Deleted"), HttpStatusCode.NotFound);
            Assert.Equal(HttpStatusCode.Forbidden, (await s.Host.Reader(identifyOnly).GetAsync(PendingDeletion)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ReportAsync(s.Host.Reader(identifyOnly), id, Guid.NewGuid(), "Deleted")).StatusCode);
            foreach (var client in new[] { s.Host.DeviceAnonymous(), s.Host.Reader(revokedReader), s.Host.Reader(s.Reader.Code, "wrong-key") })
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(PendingDeletion)).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await ReportAsync(client, id, Guid.NewGuid(), "Deleted")).StatusCode);
            }

            foreach (var json in new[]
            {
                """{"outcome":"Deleted"}""", $$"""{"attemptId":"{{Guid.Empty}}","outcome":"Deleted"}""",
                $$"""{"attemptId":"{{Guid.NewGuid()}}"}""", $$"""{"attemptId":"{{Guid.NewGuid()}}","outcome":"Other"}""",
                $$"""{"attemptId":"{{Guid.NewGuid()}}","outcome":1}""", $$"""{"attemptId":"{{Guid.NewGuid()}}","outcome":"Deleted","slot":{{Slot}}}""",
                $$"""{"attemptId":"{{Guid.NewGuid()}}","outcome":"Deleted","deviceId":"{{s.Reader.Id}}"}""",
                $$"""{"attemptId":"{{Guid.NewGuid()}}","outcome":"Deleted","patientId":"{{s.Patient.Id}}"}"""
            })
            {
                await AssertProblemAsync(await PostJsonAsync(s.ReaderClient, DeletionResult(id), json), HttpStatusCode.BadRequest);
            }

            Assert.Equal(0, Assert.Single(await JobsAsync(s.Host, referenceId)).Attempts);
            var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            Assert.Equal(HttpStatusCode.NoContent, (await ReportAsync(s.ReaderClient, id, a, "SensorError")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await ReportAsync(s.ReaderClient, id, a, "SensorError")).StatusCode);
            Assert.Equal(1, Assert.Single(await JobsAsync(s.Host, referenceId)).Attempts);
            await AssertProblemAsync(await ReportAsync(s.ReaderClient, id, a, "Deleted"), HttpStatusCode.Conflict);
            Assert.Equal(HttpStatusCode.NoContent, (await ReportAsync(s.ReaderClient, id, b, "SensorError")).StatusCode);
            var afterErrors = Assert.Single(await JobsAsync(s.Host, referenceId));
            Assert.Equal((FingerprintSlotDeletionStatus.Pending, 2, (FingerprintSlotDeletionFailure?)FingerprintSlotDeletionFailure.SensorError),
                (afterErrors.Status, afterErrors.Attempts, afterErrors.LastFailure));
            Assert.Equal(1, await AuditsAsync(s.Host, FingerprintRevocationService.DeletionEntityName, AuditActionType.Update));
            Assert.Equal("Pending", (await ListedAsync(s.Reception, s.Patient.Id, referenceId)).GetProperty("cleanup").GetString());

            Assert.Equal(HttpStatusCode.NoContent, (await ReportAsync(s.ReaderClient, id, c, "Deleted")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await ReportAsync(s.ReaderClient, id, c, "Deleted")).StatusCode);
            await AssertProblemAsync(await ReportAsync(s.ReaderClient, id, c, "NotFound"), HttpStatusCode.Conflict);
            await AssertProblemAsync(await ReportAsync(s.ReaderClient, id, Guid.NewGuid(), "Deleted"), HttpStatusCode.Conflict);
            await AssertProblemAsync(await ReportAsync(s.ReaderClient, id, Guid.NewGuid(), "SensorError"), HttpStatusCode.Conflict);
            var completed = Assert.Single(await JobsAsync(s.Host, referenceId));
            Assert.Equal((FingerprintSlotDeletionStatus.Completed, (FingerprintSlotDeletionOutcome?)FingerprintSlotDeletionOutcome.Deleted, 3, c),
                (completed.Status, completed.Outcome, completed.Attempts, completed.LastAttemptId));
            Assert.NotNull(completed.CompletedAtUtc);
            Assert.Equal(2, await AuditsAsync(s.Host, FingerprintRevocationService.DeletionEntityName, AuditActionType.Update));
            Assert.Equal("Completed", (await ListedAsync(s.Reception, s.Patient.Id, referenceId)).GetProperty("cleanup").GetString());

            // NotFound (the slot was already empty) completes a job as well.
            var other = await s.Host.Web.SeedPatientAsync("Luis", "Gómez");
            var secondId = await EnrollAsync(s, other.Id, Slot + 1);
            await RevokeAsync(s.Reception, other.Id, secondId);
            var second = Assert.Single(await JobsAsync(s.Host, secondId)).Id;
            Assert.Equal(HttpStatusCode.NoContent, (await ReportAsync(s.ReaderClient, second, Guid.NewGuid(), "NotFound")).StatusCode);
            Assert.Equal(FingerprintSlotDeletionOutcome.NotFound, Assert.Single(await JobsAsync(s.Host, secondId)).Outcome);
            Assert.Equal("Completed", (await ListedAsync(s.Reception, other.Id, secondId)).GetProperty("cleanup").GetString());
        }

        // Two reports at once: the same attempt twice is one attempt; two different terminal outcomes, one wins.
        [SkippableFact]
        public async Task ConcurrentReportsApplyOnce()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;

            for (var round = 0; round < 3; round++)
            {
                var patient = await s.Host.Web.SeedPatientAsync($"Paciente{round}");
                var referenceId = await EnrollAsync(s, patient.Id, 20 + round);
                await RevokeAsync(s.Reception, patient.Id, referenceId);
                var id = Assert.Single(await JobsAsync(s.Host, referenceId)).Id;

                var attempt = Guid.NewGuid();
                var same = await Task.WhenAll(
                    ReportAsync(s.Host.Reader(s.Reader), id, attempt, "SensorError"), ReportAsync(s.Host.Reader(s.Reader), id, attempt, "SensorError"));
                Assert.All(same, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
                Assert.Equal(1, Assert.Single(await JobsAsync(s.Host, referenceId)).Attempts);

                var different = await Task.WhenAll(
                    ReportAsync(s.Host.Reader(s.Reader), id, Guid.NewGuid(), "Deleted"), ReportAsync(s.Host.Reader(s.Reader), id, Guid.NewGuid(), "NotFound"));
                Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict], different.Select(r => r.StatusCode).Order());
                var job = Assert.Single(await JobsAsync(s.Host, referenceId));
                Assert.Equal((FingerprintSlotDeletionStatus.Completed, 2), (job.Status, job.Attempts));
            }
        }

        // D20 (deterministic race, real Revoke endpoint): the paramedic's confirmation passes all its checks (the reference is
        // still Active) and waits on the encounter row; meanwhile the Revoke endpoint commits. When the encounter is released
        // the confirmation's guard on the reference's Status finds it revoked and the whole confirmation loses.
        [SkippableFact]
        public async Task AConfirmationLosesAgainstTheRevokeEndpoint()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;
            var referenceId = await EnrollAsync(s, s.Patient.Id);
            var (paramedic, emergencyId, sessions) = await EmergencyAsync(s);
            var sessionId = (await JsonAsync(await paramedic.Client.PostAsJsonAsync(sessions, new { clientRequestId = Guid.NewGuid(), deviceId = s.Reader.Id })))
                .GetProperty("id").GetGuid();
            await PostJsonAsync(s.ReaderClient, $"/api/device/fingerprint/sessions/{sessionId}/result", $$"""{"outcome":"Match","slot":{{Slot}}}""");

            var (connection, hold) = await ClinicalRace.HoldEncounterAsync(s.Host.ConnectionString, emergencyId);
            await using (connection)
            {
                var confirm = paramedic.Client.PostAsJsonAsync($"{sessions}/{sessionId}/confirm", new { clientRequestId = Guid.NewGuid() });
                await ClinicalRace.WaitUntilWaitingOnLocksAsync(s.Host.ConnectionString, 1, confirm);
                Assert.Equal(HttpStatusCode.OK, (await RevokeAsync(s.Reception, s.Patient.Id, referenceId)).StatusCode);
                Assert.False(confirm.IsCompleted);
                await hold.RollbackAsync();

                await AssertProblemAsync(await confirm, HttpStatusCode.Conflict);
            }

            Assert.Null((await s.Host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == emergencyId))).PatientId);
            Assert.Equal(FingerprintSessionStatus.Matched,
                (await s.Host.Web.DbAsync(db => db.FingerprintIdentificationSessions.AsNoTracking().SingleAsync(x => x.Id == sessionId))).Status);
            Assert.Equal(0, await s.Host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergencyId && a.Action == AuditActionType.Identify)));
            Assert.Equal(BiometricReferenceStatus.Revoked, (await ReferenceAsync(s.Host, referenceId)).Status);
            Assert.Equal(FingerprintSlotDeletionStatus.Pending, Assert.Single(await JobsAsync(s.Host, referenceId)).Status);
            var candidate = await JsonAsync(await paramedic.Client.GetAsync($"{sessions}/{sessionId}"));
            Assert.Equal(JsonValueKind.Null, candidate.GetProperty("candidate").ValueKind);
        }

        // D21 (deterministic and free races): two revocations of the same reference both answer 200 with the same state, and
        // the database holds ONE revocation, ONE job and ONE revocation audit entry.
        [SkippableFact]
        public async Task ConcurrentRevocationsRevokeOnceWithOneJob()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;
            var referenceId = await EnrollAsync(s, s.Patient.Id);

            var (connection, hold) = await ClinicalRace.HoldReferenceAsync(s.Host.ConnectionString, referenceId);
            Task<HttpResponseMessage>[] revocations;
            await using (connection)
            {
                revocations = [RevokeAsync(s.Reception, s.Patient.Id, referenceId), RevokeAsync(s.Reception, s.Patient.Id, referenceId)];
                await ClinicalRace.WaitUntilWaitingOnLocksAsync(s.Host.ConnectionString, 2, revocations);
                await hold.RollbackAsync();
            }

            var responses = await Task.WhenAll(revocations);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
            Assert.Equal(bodies[0], bodies[1]);
            Assert.Single(await JobsAsync(s.Host, referenceId));
            Assert.Equal(1, await AuditsAsync(s.Host, nameof(PatientBiometricReference), AuditActionType.Update));
            Assert.Equal(1, await AuditsAsync(s.Host, FingerprintRevocationService.DeletionEntityName, AuditActionType.Create));

            // Without any lock help, a few rounds of plain concurrency end the same way.
            for (var round = 0; round < 3; round++)
            {
                var patient = await s.Host.Web.SeedPatientAsync($"Paciente{round}");
                var id = await EnrollAsync(s, patient.Id, 30 + round);
                var both = await Task.WhenAll(RevokeAsync(s.Reception, patient.Id, id), RevokeAsync(s.Reception, patient.Id, id));
                Assert.All(both, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
                Assert.Single(await JobsAsync(s.Host, id));
            }

            Assert.Equal(4, await AuditsAsync(s.Host, nameof(PatientBiometricReference), AuditActionType.Update));
        }

        // A revoked reader can never delete: the job stays Pending (no Abandoned), the reference stays Revoked and the Web says
        // the cleanup needs attention. Its other references are not touched.
        [SkippableFact]
        public async Task ARevokedReaderLeavesTheCleanupNeedingAttention()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;
            var referenceId = await EnrollAsync(s, s.Patient.Id);
            var other = await s.Host.Web.SeedPatientAsync("Luis", "Gómez");
            var untouched = await EnrollAsync(s, other.Id, Slot + 1);
            await RevokeAsync(s.Reception, s.Patient.Id, referenceId);

            await s.Host.RevokeReaderAsync(s.Reader.Id);

            Assert.Equal(HttpStatusCode.Unauthorized, (await s.ReaderClient.GetAsync(PendingDeletion)).StatusCode);
            var listed = await ListedAsync(s.Reception, s.Patient.Id, referenceId);
            Assert.Equal(("Revoked", "AttentionRequired"), (listed.GetProperty("status").GetString(), listed.GetProperty("cleanup").GetString()));
            Assert.Equal(FingerprintSlotDeletionStatus.Pending, Assert.Single(await JobsAsync(s.Host, referenceId)).Status);
            Assert.Equal(BiometricReferenceStatus.Active, (await ReferenceAsync(s.Host, untouched)).Status);
            Assert.Empty(await JobsAsync(s.Host, untouched));
        }

        // A legacy reference (reader and slot unknown) is revoked anyway, without a job for any reader; a reference revoked
        // before revocation existed stays history: a replay never queues a repair job (its slot may hold another template).
        [SkippableFact]
        public async Task LegacyAndHistoricalReferencesNeverQueueAJob()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;
            var legacy = PatientBiometricReference.Register(s.Patient.Id, BiometricModality.Fingerprint, "R307", "legacy-reader-7:slot:3");
            await s.Host.Web.DbAsync(async db =>
            {
                db.PatientBiometricReferences.Add(legacy);
                await db.SaveChangesAsync();
            });
            var historicalId = await EnrollAsync(s, s.Patient.Id);
            await s.Host.Web.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE clinical."PatientBiometricReferences" SET "Status" = 2, "RevokedAtUtc" = GREATEST(now(), "CreatedAtUtc") WHERE "Id" = {historicalId}"""));

            var revoked = await JsonAsync(await RevokeAsync(s.Reception, s.Patient.Id, legacy.Id));
            var history = await JsonAsync(await RevokeAsync(s.Reception, s.Patient.Id, historicalId));

            Assert.Equal(("Revoked", "AttentionRequired"), (revoked.GetProperty("status").GetString(), revoked.GetProperty("cleanup").GetString()));
            Assert.Equal(("Revoked", "NotRequired"), (history.GetProperty("status").GetString(), history.GetProperty("cleanup").GetString()));
            Assert.Equal(0, await s.Host.Web.DbAsync(db => db.FingerprintSlotDeletionJobs.CountAsync()));
            Assert.Equal(1, await AuditsAsync(s.Host, nameof(PatientBiometricReference), AuditActionType.Update));
            Assert.Equal(HttpStatusCode.NoContent, (await s.ReaderClient.GetAsync(PendingDeletion)).StatusCode);
        }

        // Nothing sensitive in the logs of the Device API and the Mobile BFF, nor in any Web body: no key, slot or subject
        // reference. The slot only appears in the reader's own pending deletion.
        [SkippableFact]
        public async Task NoKeySlotOrSubjectReferenceIsLoggedOrReturnedToTheWeb()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;
            const int slot = 6143;
            var referenceId = await EnrollAsync(s, s.Patient.Id, slot);

            var web = new List<string>
            {
                await (await RevokeAsync(s.Reception, s.Patient.Id, referenceId)).Content.ReadAsStringAsync(),
                await (await RevokeAsync(s.Reception, s.Patient.Id, referenceId)).Content.ReadAsStringAsync(),
                await (await s.Reception.Client.GetAsync(Fingerprints(s.Patient.Id))).Content.ReadAsStringAsync()
            };
            var id = Assert.Single(await JobsAsync(s.Host, referenceId)).Id;
            var device = new List<string>
            {
                await (await ReportAsync(s.ReaderClient, id, Guid.NewGuid(), "SensorError")).Content.ReadAsStringAsync(),
                await (await ReportAsync(s.Host.Reader(s.Reader.Code, "wrong-key"), id, Guid.NewGuid(), "Deleted")).Content.ReadAsStringAsync(),
                await (await ReportAsync(s.ReaderClient, Guid.NewGuid(), Guid.NewGuid(), "Deleted")).Content.ReadAsStringAsync()
            };
            Assert.Contains(slot.ToString(), await (await s.ReaderClient.GetAsync(PendingDeletion)).Content.ReadAsStringAsync());

            var logs = s.Host.Logs.TextWithoutCorrelationIds;
            foreach (var secret in new[] { s.Reader.Key, "r307:device", slot.ToString() })
            {
                Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var body in web.Concat(device).Select(RandomIds.Mask))
            {
                foreach (var secret in new[] { s.Reader.Key, "r307", slot.ToString(), "slot\":", "subjectReference", "secret", "hash", s.Patient.Id.ToString() })
                {
                    Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
                }
            }

            var audits = await s.Host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking().Where(a => a.PatientId == s.Patient.Id).ToListAsync());
            Assert.All(audits, a =>
            {
                foreach (var secret in new[] { "r307", "slot", slot.ToString(), s.Reader.Key, "subjectReference" })
                {
                    Assert.DoesNotContain(secret, a.ChangesJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                }
            });
        }

        // The table's own rules: one job per reference, its lifecycle, and the biometric history cannot be deleted under it.
        [SkippableFact]
        public async Task TheDatabaseEnforcesOneJobPerReferenceAndItsLifecycle()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;
            var referenceId = await EnrollAsync(s, s.Patient.Id);
            await RevokeAsync(s.Reception, s.Patient.Id, referenceId);
            var other = await s.Host.Web.SeedPatientAsync("Luis", "Gómez");
            var otherId = await EnrollAsync(s, other.Id, Slot + 1);

            async Task<string?> ExecuteAsync(FormattableString sql)
            {
                try
                {
                    await s.Host.Web.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql));
                    return null;
                }
                catch (PostgresException exception)
                {
                    return exception.ConstraintName;
                }
            }

            FormattableString Insert(Guid reference, int status, int? outcome = null, int attempts = 0) =>
                $"""INSERT INTO clinical."FingerprintSlotDeletionJobs" ("Id", "BiometricReferenceId", "DeviceCredentialId", "Status", "Outcome", "Attempts", "CreatedAtUtc") VALUES ({Guid.NewGuid()}, {reference}, {s.Reader.Id}, {status}, {outcome}, {attempts}, now())""";

            Assert.Equal("UX_FingerprintSlotDeletionJobs_Reference", await ExecuteAsync(Insert(referenceId, 1)));
            Assert.Equal("CK_FingerprintSlotDeletionJobs_Lifecycle", await ExecuteAsync(Insert(otherId, 2, 1)));
            Assert.Equal("CK_FingerprintSlotDeletionJobs_Lifecycle", await ExecuteAsync(Insert(otherId, 1, 1)));
            Assert.Equal("CK_FingerprintSlotDeletionJobs_Attempts", await ExecuteAsync(Insert(otherId, 1, attempts: 1)));
            Assert.Equal("FK_FingerprintSlotDeletionJobs_PatientBiometricReferences_Biom~", await ExecuteAsync(Insert(Guid.NewGuid(), 1)));
            Assert.NotNull(await ExecuteAsync($"""DELETE FROM clinical."PatientBiometricReferences" WHERE "Id" = {referenceId}"""));
            Assert.Null(await ExecuteAsync(Insert(otherId, 1)));
        }
    }
}
