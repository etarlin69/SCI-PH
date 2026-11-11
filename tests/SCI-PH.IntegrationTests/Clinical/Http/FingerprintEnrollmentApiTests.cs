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
    // Fingerprint enrollment over the real hosts and PostgreSQL: hospital staff (Web BFF) open an enrollment on a reader
    // that can enroll and identify, the reader (Device API) stores the template on its sensor and reports the slot, and
    // the reference then works for an emergency identification (Mobile BFF) on the same reader. Nothing about the patient
    // reaches the reader; nothing about the slot or the subject reference reaches the Web; results, replays, conflicts,
    // expiry and races resolve to one state; the confirmation of an identification loses against a revocation.
    [Collection(PostgresCollection.Name)]
    public sealed class FingerprintEnrollmentApiTests
    {
        private const string Enrollers = "/api/web/fingerprint-enrollers";
        private const string Pending = "/api/device/fingerprint/enrollments/pending";
        private const int Slot = 5;
        private readonly PostgresFixture _postgres;

        public FingerprintEnrollmentApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static string Fingerprints(Guid patientId) => $"/api/web/patients/{patientId}/fingerprints";

        private static string Enrollments(Guid patientId) => $"{Fingerprints(patientId)}/enrollments";

        private static string Result(Guid enrollmentId) => $"/api/device/fingerprint/enrollments/{enrollmentId}/result";

        private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, string json) =>
            client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

        private static Task<HttpResponseMessage> OpenAsync(StaffSession staff, Guid patientId, Guid readerId, Guid? key = null) =>
            staff.Client.PostAsJsonAsync(Enrollments(patientId), new { clientRequestId = key ?? Guid.NewGuid(), deviceId = readerId });

        private static async Task<Guid> OpenedAsync(StaffSession staff, Guid patientId, Guid readerId)
        {
            var response = await OpenAsync(staff, patientId, readerId);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await JsonAsync(response)).GetProperty("id").GetGuid();
        }

        private static Task<HttpResponseMessage> SubmitAsync(HttpClient reader, Guid enrollmentId, string outcome, int? slot = null) =>
            PostJsonAsync(reader, Result(enrollmentId), slot is null ? $$"""{"outcome":"{{outcome}}"}""" : $$"""{"outcome":"{{outcome}}","slot":{{slot}}}""");

        private static async Task<JsonElement> StatusAsync(StaffSession staff, Guid patientId, Guid enrollmentId)
        {
            var response = await staff.Client.GetAsync($"{Enrollments(patientId)}/{enrollmentId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await JsonAsync(response);
        }

        private static Task<HttpResponseMessage> CancelAsync(StaffSession staff, Guid patientId, Guid enrollmentId) =>
            staff.Client.PostAsync($"{Enrollments(patientId)}/{enrollmentId}/cancel", null);

        private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return problem.RootElement.Clone();
        }

        private static Task<List<PatientBiometricReference>> ReferencesAsync(MobileEmergencyHost host, Guid patientId) =>
            host.Web.DbAsync(db => db.PatientBiometricReferences.AsNoTracking().Where(r => r.PatientId == patientId).ToListAsync());

        private static Task<FingerprintEnrollment> StoredAsync(MobileEmergencyHost host, Guid enrollmentId) =>
            host.Web.DbAsync(async db => (await db.FingerprintEnrollmentSessions.AsNoTracking().SingleAsync(e => e.Id == enrollmentId)).ToEnrollment());

        private static Task RevokeAsync(MobileEmergencyHost host, Guid referenceId) =>
            host.Web.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE clinical."PatientBiometricReferences" SET "Status" = 2, "RevokedAtUtc" = GREATEST(now(), "CreatedAtUtc") WHERE "Id" = {referenceId}"""));

        private static Task ExpireAsync(MobileEmergencyHost host, Guid enrollmentId) =>
            host.Web.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE clinical."FingerprintEnrollmentSessions" SET "CreatedAtUtc" = now() - interval '5 minutes', "ExpiresAtUtc" = now() - interval '1 minute' WHERE "Id" = {enrollmentId}"""));

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

        // The whole path: enroll on the Web with the reader, then identify an NN emergency on the Mobile with THAT reader
        // and slot, without any seeded reference.
        [SkippableFact]
        public async Task AnEnrolledFingerprintIdentifiesAnEmergencyOnTheSameReader()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, clinic, reception, patient, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            Assert.Empty(await ReferencesAsync(host, patient.Id));

            var enrollers = await JsonAsync(await reception.Client.GetAsync(Enrollers));
            var enroller = Assert.Single(enrollers.EnumerateArray());
            Assert.Equal(["id", "code", "name", "lastSeenAtUtc"], enroller.EnumerateObject().Select(p => p.Name));
            Assert.Equal(reader.Id, enroller.GetProperty("id").GetGuid());

            var key = Guid.NewGuid();
            var openResponse = await OpenAsync(reception, patient.Id, reader.Id, key);
            Assert.Equal(HttpStatusCode.Created, openResponse.StatusCode);
            var opened = await JsonAsync(openResponse);
            var enrollmentId = opened.GetProperty("id").GetGuid();
            Assert.Equal("Pending", opened.GetProperty("status").GetString());
            var stored = await StoredAsync(host, enrollmentId);
            Assert.Equal((TimeSpan.FromSeconds(120), reception.StaffId!.Value), (stored.ExpiresAtUtc - stored.CreatedAtUtc, stored.RequestedByStaffId));
            Assert.Equal(HttpStatusCode.OK, (await OpenAsync(reception, patient.Id, reader.Id, key)).StatusCode);

            var pendingRaw = await (await readerClient.GetAsync(Pending)).Content.ReadAsStringAsync();
            var pending = JsonDocument.Parse(pendingRaw).RootElement;
            Assert.Equal(["enrollmentId", "expiresAtUtc"], pending.EnumerateObject().Select(p => p.Name));
            foreach (var secret in new[] { patient.Id.ToString(), "Ana", "Pérez" })
            {
                Assert.DoesNotContain(secret, pendingRaw, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, enrollmentId, "Enrolled", Slot)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, enrollmentId, "Enrolled", Slot)).StatusCode);

            var reference = Assert.Single(await ReferencesAsync(host, patient.Id));
            Assert.Equal((BiometricReferenceStatus.Active, "R307", FingerprintReferences.SubjectReference(reader.Id, Slot), reception.StaffId, (string?)null),
                (reference.Status, reference.Provider, reference.SubjectReference, reference.RegisteredByStaffId, reference.AlgorithmVersion));
            var status = await StatusAsync(reception, patient.Id, enrollmentId);
            Assert.Equal(("Enrolled", reference.Id), (status.GetProperty("status").GetString(), status.GetProperty("biometricReferenceId").GetGuid()));
            var listedRaw = await (await reception.Client.GetAsync(Fingerprints(patient.Id))).Content.ReadAsStringAsync();
            var listed = Assert.Single(JsonDocument.Parse(listedRaw).RootElement.EnumerateArray());
            Assert.Equal(["id", "status", "reader", "registeredAtUtc", "revokedAtUtc", "cleanup"], listed.EnumerateObject().Select(p => p.Name));
            Assert.Equal((reader.Code, true), (listed.GetProperty("reader").GetProperty("code").GetString(), listed.GetProperty("reader").GetProperty("available").GetBoolean()));
            Assert.DoesNotContain("r307", listedRaw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(reader.Id.ToString(), listedRaw);
            Assert.Equal(0, await host.Web.DbAsync(db => db.IdentificationAttempts.CountAsync()));
            var audits = await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking().Where(a => a.PatientId == patient.Id).ToListAsync());
            Assert.Single(audits, a => a.EntityName == FingerprintEnrollmentService.EnrollmentEntityName && a.Action == AuditActionType.Create);
            Assert.Single(audits, a => a.EntityName == nameof(PatientBiometricReference) && a.Action == AuditActionType.Create);
            Assert.All(audits, a => Assert.DoesNotContain("r307", a.ChangesJson ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            // The emergency identification with the newly enrolled reference.
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var emergency = await (await paramedic.Client.PostAsJsonAsync("/api/mobile/emergencies", new
            {
                clientRequestId = Guid.NewGuid(),
                startedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                chiefComplaint = "Inconsciente"
            })).ReadAsAsync<PrehospitalEmergencySummary>();
            var session = await JsonAsync(await paramedic.Client.PostAsJsonAsync(
                $"/api/mobile/emergencies/{emergency.Id}/identify/fingerprint/sessions", new { clientRequestId = Guid.NewGuid(), deviceId = reader.Id }));
            var sessionId = session.GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.NoContent, (await PostJsonAsync(readerClient,
                $"/api/device/fingerprint/sessions/{sessionId}/result", $$"""{"outcome":"Match","slot":{{Slot}}}""")).StatusCode);
            var matched = await JsonAsync(await paramedic.Client.GetAsync($"/api/mobile/emergencies/{emergency.Id}/identify/fingerprint/sessions/{sessionId}"));
            Assert.Equal(("Matched", "Ana Pérez"), (matched.GetProperty("status").GetString(), matched.GetProperty("candidate").GetProperty("displayName").GetString()));
            var confirm = await paramedic.Client.PostAsJsonAsync(
                $"/api/mobile/emergencies/{emergency.Id}/identify/fingerprint/sessions/{sessionId}/confirm", new { clientRequestId = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            Assert.Equal(patient.Id, (await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == emergency.Id))).PatientId);
            Assert.Equal(1, await host.Web.DbAsync(db => db.IdentificationAttempts.CountAsync(a => a.BiometricReferenceId == reference.Id)));
        }

        [SkippableFact]
        public async Task OnlyAReaderWithTheEnrollScopeEnrollsAndOnlyOneWithBothScopesIsOffered()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var enrollOnly = await host.ProvisionReaderAsync(DeviceScopes.FingerprintEnroll);
            var identifyOnly = await host.ProvisionReaderAsync(DeviceScopes.FingerprintIdentify);
            var revoked = await host.ProvisionReaderAsync(DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            await host.RevokeReaderAsync(revoked.Id);
            var enrollmentId = await OpenedAsync(reception, patient.Id, reader.Id);

            var offered = (await JsonAsync(await reception.Client.GetAsync(Enrollers))).EnumerateArray().Select(r => r.GetProperty("id").GetGuid());
            Assert.Equal([reader.Id], offered);
            foreach (var other in new[] { enrollOnly, identifyOnly, revoked })
            {
                await AssertProblemAsync(await OpenAsync(reception, patient.Id, other.Id), HttpStatusCode.NotFound);
            }

            // Enroll-only can use the Device API (it is not offered on the Web); Identify-only cannot.
            Assert.Equal(HttpStatusCode.NoContent, (await host.Reader(enrollOnly).GetAsync(Pending)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.Reader(identifyOnly).GetAsync(Pending)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SubmitAsync(host.Reader(identifyOnly), enrollmentId, "Enrolled", Slot)).StatusCode);
            foreach (var client in new[]
            {
                host.DeviceAnonymous(), host.Reader(reader.Code, "wrong-key"), host.Reader(revoked),
                host.DeviceAnonymous().WithBearer(reception.Client.DefaultRequestHeaders.Authorization!.Parameter!)
            })
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Pending)).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await SubmitAsync(client, enrollmentId, "Enrolled", Slot)).StatusCode);
            }

            // Another reader's enrollment is not found, whatever the body; the reader comes from the credential only.
            var foreign = await AssertProblemAsync(await SubmitAsync(host.Reader(enrollOnly), enrollmentId, "Enrolled", Slot), HttpStatusCode.NotFound);
            var missing = await AssertProblemAsync(await SubmitAsync(readerClient, Guid.NewGuid(), "Enrolled", Slot), HttpStatusCode.NotFound);
            Assert.Equal(foreign.GetProperty("detail").GetString(), missing.GetProperty("detail").GetString());
            foreach (var json in new[]
            {
                """{"outcome":"Enrolled"}""", """{"outcome":"Enrolled","slot":-1}""", """{"outcome":"Failed","slot":1}""",
                """{"outcome":"Other"}""", $$"""{"outcome":"Enrolled","slot":1,"deviceId":"{{reader.Id}}"}""",
                """{"outcome":"Enrolled","slot":1,"template":"AAAA"}""", $$"""{"outcome":"Enrolled","slot":1,"patientId":"{{patient.Id}}"}"""
            })
            {
                await AssertProblemAsync(await PostJsonAsync(readerClient, Result(enrollmentId), json), HttpStatusCode.BadRequest);
            }

            Assert.Equal(FingerprintEnrollmentStatus.Pending, (await StoredAsync(host, enrollmentId)).Status);
            Assert.Empty(await ReferencesAsync(host, patient.Id));
        }

        // Biometrics.Manage (Receptionist, Administrator) AND active clinical staff who can see the patient; no role bypass.
        [SkippableFact]
        public async Task OnlyStaffWithBiometricsManageEnrolls()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, clinic, _, patient, reader, _) = await SetupAsync();
            await using var _host = host;

            foreach (var (role, type) in new[]
            {
                (SecurityRoles.Doctor, ClinicalStaffType.Physician), (SecurityRoles.Nurse, ClinicalStaffType.Nurse),
                (SecurityRoles.Paramedic, ClinicalStaffType.Paramedic)
            })
            {
                var staff = await host.Web.SignInAsync(role, [clinic], staffType: type);
                Assert.Equal(HttpStatusCode.Forbidden, (await staff.Client.GetAsync(Enrollers)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await staff.Client.GetAsync(Fingerprints(patient.Id))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await OpenAsync(staff, patient.Id, reader.Id)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Web.Anonymous().GetAsync(Enrollers)).StatusCode);
            var adminWithoutStaff = await host.Web.SignInAsync(SecurityRoles.Administrator, [], asClinicalStaff: false);
            await AssertProblemAsync(await OpenAsync(adminWithoutStaff, patient.Id, reader.Id), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await adminWithoutStaff.Client.GetAsync(Enrollers), HttpStatusCode.Forbidden);
            var inactiveReception = await host.Web.SignInAsync(SecurityRoles.Receptionist, [clinic], staffActive: false);
            await AssertProblemAsync(await OpenAsync(inactiveReception, patient.Id, reader.Id), HttpStatusCode.Forbidden);

            var admin = await host.Web.SignInAsync(SecurityRoles.Administrator, [clinic]);
            Assert.Equal(HttpStatusCode.Created, (await OpenAsync(admin, patient.Id, reader.Id)).StatusCode);
            await AssertProblemAsync(await OpenAsync(admin, Guid.NewGuid(), reader.Id), HttpStatusCode.NotFound);
        }

        [SkippableFact]
        public async Task OpeningIsIdempotentAndOneEnrollmentIsPendingPerPatientAndReader()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var otherReader = await host.ProvisionReaderAsync(DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            var otherPatient = await host.Web.SeedPatientAsync("Luis", "Gómez");
            var key = Guid.NewGuid();

            Assert.Equal(HttpStatusCode.Created, (await OpenAsync(reception, patient.Id, reader.Id, key)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await OpenAsync(reception, patient.Id, reader.Id, key)).StatusCode);
            await AssertProblemAsync(await OpenAsync(reception, patient.Id, otherReader.Id, key), HttpStatusCode.Conflict);
            await AssertProblemAsync(await OpenAsync(reception, patient.Id, otherReader.Id), HttpStatusCode.Conflict);
            await AssertProblemAsync(await OpenAsync(reception, otherPatient.Id, reader.Id), HttpStatusCode.Conflict);
            foreach (var json in new[]
            {
                $$"""{"clientRequestId":"{{Guid.NewGuid()}}"}""",
                $$"""{"clientRequestId":"{{Guid.Empty}}","deviceId":"{{otherReader.Id}}"}""",
                $$"""{"clientRequestId":"{{Guid.NewGuid()}}","deviceId":"{{otherReader.Id}}","patientId":"{{patient.Id}}"}""",
                $$"""{"clientRequestId":"{{Guid.NewGuid()}}","deviceId":"{{otherReader.Id}}","slot":1}""",
                $$"""{"clientRequestId":"{{Guid.NewGuid()}}","deviceId":"{{otherReader.Id}}","staffId":"{{Guid.NewGuid()}}"}"""
            })
            {
                await AssertProblemAsync(await PostJsonAsync(reception.Client, Enrollments(otherPatient.Id), json), HttpStatusCode.BadRequest);
            }

            // Concurrent opens: the same key opens one enrollment; different keys on one reader, one 201 and one 409.
            var key2 = Guid.NewGuid();
            var sameKey = await Task.WhenAll(OpenAsync(reception, otherPatient.Id, otherReader.Id, key2), OpenAsync(reception, otherPatient.Id, otherReader.Id, key2));
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created], sameKey.Select(r => r.StatusCode).Order());
            var thirdReader = await host.ProvisionReaderAsync(DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            var third = await host.Web.SeedPatientAsync("Eva", "Ruiz");
            var fourth = await host.Web.SeedPatientAsync("Juan", "Díaz");
            var differentKeys = await Task.WhenAll(OpenAsync(reception, third.Id, thirdReader.Id), OpenAsync(reception, fourth.Id, thirdReader.Id));
            Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], differentKeys.Select(r => r.StatusCode).Order());
            Assert.Equal(3, await host.Web.DbAsync(db => db.FingerprintEnrollmentSessions.CountAsync()));
            Assert.Equal(3, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == FingerprintEnrollmentService.OpenOperation)));
        }

        // Results: failures are terminal, a changed result is rejected, an active slot is a conflict that leaves the
        // enrollment waiting, and expiry or cancellation reject any result (the reader then deletes its slot).
        [SkippableFact]
        public async Task ResultsResolveOnceAndRejectionsLeaveNoReference()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient, reader, readerClient) = await SetupAsync();
            await using var _host = host;

            foreach (var (outcome, failure) in new[] { ("Failed", "Failed"), ("LowQuality", "LowQuality"), ("SensorError", "SensorError") })
            {
                var id = await OpenedAsync(reception, patient.Id, reader.Id);
                Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, id, outcome)).StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, id, outcome)).StatusCode);
                await AssertProblemAsync(await SubmitAsync(readerClient, id, "Enrolled", Slot), HttpStatusCode.Conflict);
                var status = await StatusAsync(reception, patient.Id, id);
                Assert.Equal(("Failed", failure), (status.GetProperty("status").GetString(), status.GetProperty("failure").GetString()));
            }

            // Slot already active on this reader (another patient): 409, still Pending, another slot works.
            var other = await host.Web.SeedPatientAsync("Luis", "Gómez");
            var takenId = await OpenedAsync(reception, other.Id, reader.Id);
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, takenId, "Enrolled", Slot)).StatusCode);
            var conflictId = await OpenedAsync(reception, patient.Id, reader.Id);
            await AssertProblemAsync(await SubmitAsync(readerClient, conflictId, "Enrolled", Slot), HttpStatusCode.Conflict);
            Assert.Equal(FingerprintEnrollmentStatus.Pending, (await StoredAsync(host, conflictId)).Status);
            Assert.Empty(await ReferencesAsync(host, patient.Id));
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, conflictId, "Enrolled", Slot + 1)).StatusCode);
            await AssertProblemAsync(await SubmitAsync(readerClient, conflictId, "Enrolled", Slot + 2), HttpStatusCode.Conflict);
            Assert.Single(await ReferencesAsync(host, patient.Id));

            // The patient already has an active fingerprint on this reader: 409 when opening.
            await AssertProblemAsync(await OpenAsync(reception, patient.Id, reader.Id), HttpStatusCode.Conflict);

            // Expired and cancelled enrollments reject the result.
            var third = await host.Web.SeedPatientAsync("Eva", "Ruiz");
            var expiredId = await OpenedAsync(reception, third.Id, reader.Id);
            await ExpireAsync(host, expiredId);
            Assert.Equal(HttpStatusCode.NoContent, (await readerClient.GetAsync(Pending)).StatusCode);
            await AssertProblemAsync(await SubmitAsync(readerClient, expiredId, "Enrolled", 9), HttpStatusCode.Conflict);
            Assert.Equal("Expired", (await StatusAsync(reception, third.Id, expiredId)).GetProperty("status").GetString());
            var cancelledId = await OpenedAsync(reception, third.Id, reader.Id);
            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(reception, third.Id, cancelledId)).StatusCode);
            await AssertProblemAsync(await SubmitAsync(readerClient, cancelledId, "Enrolled", 9), HttpStatusCode.Conflict);
            Assert.Empty(await ReferencesAsync(host, third.Id));
        }

        [SkippableFact]
        public async Task ConcurrentResultsAcceptExactlyOne()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, _, reader, _) = await SetupAsync();
            await using var _host = host;

            for (var round = 0; round < 3; round++)
            {
                var patient = await host.Web.SeedPatientAsync($"Paciente{round}");
                var id = await OpenedAsync(reception, patient.Id, reader.Id);

                var responses = await Task.WhenAll(
                    SubmitAsync(host.Reader(reader), id, "Enrolled", 10 + round), SubmitAsync(host.Reader(reader), id, "Failed"));

                Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
                var stored = await StoredAsync(host, id);
                Assert.Equal(stored.Status == FingerprintEnrollmentStatus.Enrolled ? 1 : 0, (await ReferencesAsync(host, patient.Id)).Count);
            }
        }

        [SkippableFact]
        public async Task CancellingEndsAPendingEnrollmentOnceAndNeverACompletedOne()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient, reader, readerClient) = await SetupAsync();
            await using var _host = host;

            var pendingId = await OpenedAsync(reception, patient.Id, reader.Id);
            var first = await CancelAsync(reception, patient.Id, pendingId);
            var again = await CancelAsync(reception, patient.Id, pendingId);
            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, again.StatusCode));
            Assert.Equal("Cancelled", (await JsonAsync(again)).GetProperty("status").GetString());
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.EntityName == FingerprintEnrollmentService.EnrollmentEntityName && a.Action == AuditActionType.Update)));

            var failedId = await OpenedAsync(reception, patient.Id, reader.Id);
            await SubmitAsync(readerClient, failedId, "SensorError");
            Assert.Equal("Failed", (await JsonAsync(await CancelAsync(reception, patient.Id, failedId))).GetProperty("status").GetString());

            var enrolledId = await OpenedAsync(reception, patient.Id, reader.Id);
            await SubmitAsync(readerClient, enrolledId, "Enrolled", Slot);
            await AssertProblemAsync(await CancelAsync(reception, patient.Id, enrolledId), HttpStatusCode.Conflict);
            Assert.Equal(BiometricReferenceStatus.Active, Assert.Single(await ReferencesAsync(host, patient.Id)).Status);

            var other = await host.Web.SeedPatientAsync("Luis", "Gómez");
            await AssertProblemAsync(await reception.Client.GetAsync($"{Enrollments(other.Id)}/{enrolledId}"), HttpStatusCode.NotFound);
        }

        // D5: a revoked reference stays as history and its slot can be enrolled again (unique among ACTIVE references only).
        [SkippableFact]
        public async Task ARevokedReferenceKeepsItsHistoryAndFreesItsSlot()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var first = await OpenedAsync(reception, patient.Id, reader.Id);
            await SubmitAsync(readerClient, first, "Enrolled", Slot);
            var revoked = Assert.Single(await ReferencesAsync(host, patient.Id));
            await RevokeAsync(host, revoked.Id);

            var again = await OpenedAsync(reception, patient.Id, reader.Id);
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(readerClient, again, "Enrolled", Slot)).StatusCode);

            var references = await ReferencesAsync(host, patient.Id);
            Assert.Equal(2, references.Count);
            Assert.Single(references, r => r.Status == BiometricReferenceStatus.Revoked);
            Assert.Single(references, r => r.Status == BiometricReferenceStatus.Active);
            Assert.All(references, r => Assert.Equal(FingerprintReferences.SubjectReference(reader.Id, Slot), r.SubjectReference));
            var listed = (await JsonAsync(await reception.Client.GetAsync(Fingerprints(patient.Id)))).EnumerateArray().ToList();
            Assert.Equal(["Active", "Revoked"], listed.Select(r => r.GetProperty("status").GetString()));

            // The database itself refuses a second ACTIVE reference with the same subject.
            var otherPatient = await host.Web.SeedPatientAsync("Luis", "Gómez");
            var duplicate = PatientBiometricReference.Register(otherPatient.Id, BiometricModality.Fingerprint, "R307", FingerprintReferences.SubjectReference(reader.Id, Slot));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => host.Web.DbAsync(async db =>
            {
                db.PatientBiometricReferences.Add(duplicate);
                await db.SaveChangesAsync();
            }));
            Assert.Equal("UX_PatientBiometricReferences_ActiveSubject", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
        }

        // D6 (deterministic race): the reference of a Matched identification is revoked in a transaction that stays open
        // while the paramedic confirms; the confirmation waits on the reference row and loses when the revocation commits.
        [SkippableFact]
        public async Task AConfirmationLosesAgainstAConcurrentRevocationOfItsReference()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, clinic, reception, patient, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var enrollmentId = await OpenedAsync(reception, patient.Id, reader.Id);
            await SubmitAsync(readerClient, enrollmentId, "Enrolled", Slot);
            var reference = Assert.Single(await ReferencesAsync(host, patient.Id));
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var emergency = await (await paramedic.Client.PostAsJsonAsync("/api/mobile/emergencies", new
            {
                clientRequestId = Guid.NewGuid(),
                startedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                chiefComplaint = "Inconsciente"
            })).ReadAsAsync<PrehospitalEmergencySummary>();
            var sessions = $"/api/mobile/emergencies/{emergency.Id}/identify/fingerprint/sessions";
            var sessionId = (await JsonAsync(await paramedic.Client.PostAsJsonAsync(sessions, new { clientRequestId = Guid.NewGuid(), deviceId = reader.Id })))
                .GetProperty("id").GetGuid();
            await PostJsonAsync(readerClient, $"/api/device/fingerprint/sessions/{sessionId}/result", $$"""{"outcome":"Match","slot":{{Slot}}}""");

            var (connection, revoke) = await ClinicalRace.BeginRevokeReferenceAsync(host.ConnectionString, reference.Id);
            await using (connection)
            {
                var confirm = paramedic.Client.PostAsJsonAsync($"{sessions}/{sessionId}/confirm", new { clientRequestId = Guid.NewGuid() });
                await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, confirm);
                await revoke.CommitAsync();
                await AssertProblemAsync(await confirm, HttpStatusCode.Conflict);
            }

            Assert.Null((await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == emergency.Id))).PatientId);
            Assert.Equal(FingerprintSessionStatus.Matched,
                (await host.Web.DbAsync(db => db.FingerprintIdentificationSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId))).Status);
            Assert.Equal(0, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergency.Id && a.Action == AuditActionType.Identify)));
            await AssertProblemAsync(await paramedic.Client.PostAsJsonAsync($"{sessions}/{sessionId}/confirm", new { clientRequestId = Guid.NewGuid() }), HttpStatusCode.Conflict);
        }

        // Nothing sensitive in the logs of the hosts or in the responses: no key, slot or subject reference.
        [SkippableFact]
        public async Task NoKeySlotOrSubjectReferenceIsLoggedOrReturned()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient, reader, readerClient) = await SetupAsync();
            await using var _host = host;
            var id = await OpenedAsync(reception, patient.Id, reader.Id);
            var bodies = new List<string>
            {
                await (await readerClient.GetAsync(Pending)).Content.ReadAsStringAsync(),
                await (await SubmitAsync(readerClient, id, "Enrolled", 6131)).Content.ReadAsStringAsync(),
                await (await SubmitAsync(readerClient, id, "Enrolled", 6132)).Content.ReadAsStringAsync(),
                await (await SubmitAsync(host.Reader(reader.Code, "wrong-key"), id, "Enrolled", 6131)).Content.ReadAsStringAsync(),
                await (await reception.Client.GetAsync(Fingerprints(patient.Id))).Content.ReadAsStringAsync(),
                await (await reception.Client.GetAsync($"{Enrollments(patient.Id)}/{id}")).Content.ReadAsStringAsync(),
                await (await reception.Client.GetAsync(Enrollers)).Content.ReadAsStringAsync()
            };

            var logs = host.Logs.TextWithoutCorrelationIds;
            foreach (var secret in new[] { reader.Key, "r307:device", "6131" })
            {
                Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var body in bodies.Select(RandomIds.Mask))
            {
                foreach (var secret in new[] { reader.Key, "r307", "6131", "slot\":", "subjectReference", "secret", "hash" })
                {
                    Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
                }
            }

            Assert.DoesNotContain(patient.Id.ToString(), bodies[0]);
        }

        // The table's own rules: one Pending enrollment per patient and per reader, and its lifecycle.
        [SkippableFact]
        public async Task TheDatabaseEnforcesOnePendingEnrollmentPerPatientAndPerReader()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var (host, _, reception, patient, reader, _) = await SetupAsync();
            await using var _host = host;
            await OpenedAsync(reception, patient.Id, reader.Id);
            var other = await host.Web.SeedPatientAsync("Luis", "Gómez");

            async Task<string?> InsertAsync(Guid patientId, Guid readerId, int status = 1)
            {
                try
                {
                    await host.Web.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
                        $"""INSERT INTO clinical."FingerprintEnrollmentSessions" ("Id", "PatientId", "DeviceCredentialId", "RequestedByStaffId", "Status", "CreatedAtUtc", "ExpiresAtUtc") VALUES ({Guid.NewGuid()}, {patientId}, {readerId}, {reception.StaffId!.Value}, {status}, now(), now() + interval '120 seconds')"""));
                    return null;
                }
                catch (PostgresException exception)
                {
                    return exception.ConstraintName;
                }
            }

            Assert.Equal("UX_FingerprintEnrollmentSessions_PendingPerPatient", await InsertAsync(patient.Id, Guid.NewGuid()));
            Assert.Equal("UX_FingerprintEnrollmentSessions_PendingPerReader", await InsertAsync(other.Id, reader.Id));
            Assert.Equal("CK_FingerprintEnrollmentSessions_Lifecycle", await InsertAsync(other.Id, Guid.NewGuid(), status: 2));
            Assert.Null(await InsertAsync(other.Id, Guid.NewGuid()));
        }
    }
}
