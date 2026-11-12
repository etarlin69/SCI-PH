using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Device.API.Controllers;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Infrastructure.Security;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // The reader's slot inventory over the real hosts and PostgreSQL (ADR-010 §10): the slots SCI-PH considers occupied on
    // the sensor of the authenticated reader, i.e. its active fingerprint references plus the slots still waiting for a
    // deletion; never another reader's, a face reference, a completed deletion or a reference it cannot read. Only slot
    // numbers reach the reader and none reaches the logs.
    [Collection(PostgresCollection.Name)]
    public sealed class FingerprintSlotInventoryApiTests
    {
        private const string Slots = "/api/device/fingerprint/slots";
        private readonly PostgresFixture _postgres;

        public FingerprintSlotInventoryApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static string Fingerprints(Guid patientId) => $"/api/web/patients/{patientId}/fingerprints";

        private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, string json) =>
            client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

        private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        // 200 and exactly {"slots":[...]} with integers only.
        private static async Task<int[]> InventoryAsync(HttpClient reader)
        {
            var response = await reader.GetAsync(Slots);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await JsonAsync(response);
            Assert.Equal(["slots"], body.EnumerateObject().Select(p => p.Name));
            return body.GetProperty("slots").EnumerateArray().Select(s => s.GetInt32()).ToArray();
        }

        private sealed record Setup(MobileEmergencyHost Host, StaffSession Reception, TestReader Reader, HttpClient ReaderClient);

        private async Task<Setup> SetupAsync()
        {
            var host = await MobileEmergencyHost.StartAsync(_postgres);
            var clinic = await host.Web.CreateClinicAsync();
            var reception = await host.Web.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var reader = await host.ProvisionReaderAsync(DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            return new Setup(host, reception, reader, host.Reader(reader));
        }

        // A new patient enrolled on the reader in `slot` through the real endpoints; returns the patient and reference ids.
        private static async Task<(Guid PatientId, Guid ReferenceId)> EnrollAsync(Setup s, int slot, TestReader? reader = null)
        {
            var on = reader ?? s.Reader;
            var patient = await s.Host.Web.SeedPatientAsync($"Paciente{slot}");
            var opened = await s.Reception.Client.PostAsJsonAsync($"{Fingerprints(patient.Id)}/enrollments", new { clientRequestId = Guid.NewGuid(), deviceId = on.Id });
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
            var enrollmentId = (await JsonAsync(opened)).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.NoContent, (await PostJsonAsync(s.Host.Reader(on),
                $"/api/device/fingerprint/enrollments/{enrollmentId}/result", $$"""{"outcome":"Enrolled","slot":{{slot}}}""")).StatusCode);
            var referenceId = (await s.Host.Web.DbAsync(db => db.FingerprintEnrollmentSessions.AsNoTracking().SingleAsync(e => e.Id == enrollmentId))).BiometricReferenceId!.Value;
            return (patient.Id, referenceId);
        }

        // Revokes through the Web and returns the deletion job id.
        private static async Task<Guid> RevokeAsync(Setup s, Guid patientId, Guid referenceId)
        {
            Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(s.Reception.Client, $"{Fingerprints(patientId)}/{referenceId}/revoke", "{}")).StatusCode);
            return (await s.Host.Web.DbAsync(db => db.FingerprintSlotDeletionJobs.AsNoTracking().SingleAsync(j => j.BiometricReferenceId == referenceId))).Id;
        }

        private static Task<HttpResponseMessage> ReportDeletionAsync(Setup s, Guid deletionId, string outcome) =>
            PostJsonAsync(s.ReaderClient, $"/api/device/fingerprint/deletions/{deletionId}/result",
                $$"""{"attemptId":"{{Guid.NewGuid()}}","outcome":"{{outcome}}"}""");

        private static Task AddReferencesAsync(Setup s, params PatientBiometricReference[] references) =>
            s.Host.Web.DbAsync(async db =>
            {
                db.PatientBiometricReferences.AddRange(references);
                await db.SaveChangesAsync();
            });

        // Active slots, sorted and unique; a revoked slot stays while its deletion is Pending (also after a sensor error) and
        // leaves only once the deletion completes. Another reader's slots, face references and other providers never count.
        [SkippableFact]
        public async Task TheInventoryHoldsTheReadersActiveAndPendingDeletionSlotsSortedAndUnique()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;

            Assert.Empty(await InventoryAsync(s.ReaderClient));
            Assert.Equal("""{"slots":[]}""", await (await s.ReaderClient.GetAsync(Slots)).Content.ReadAsStringAsync());

            var (first, firstReference) = await EnrollAsync(s, 1);
            Assert.Equal(new[] { 1 }, await InventoryAsync(s.ReaderClient));

            var (seventh, seventhReference) = await EnrollAsync(s, 7);
            await EnrollAsync(s, 22);
            await EnrollAsync(s, 0);
            var other = await s.Host.ProvisionReaderAsync(DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            await EnrollAsync(s, 3, other);
            await EnrollAsync(s, 7, other);
            var patient = await s.Host.Web.SeedPatientAsync("Rosa");
            var faceLookalike = await s.Host.Web.SeedPatientAsync("Marta");
            await AddReferencesAsync(s,
                PatientBiometricReference.Register(patient.Id, BiometricModality.Face, "FaceProvider", "face-subject-1"),
                PatientBiometricReference.Register(faceLookalike.Id, BiometricModality.Face, FingerprintReferences.Provider, FingerprintReferences.SubjectReference(s.Reader.Id, 55)),
                PatientBiometricReference.Register(patient.Id, BiometricModality.Fingerprint, "OtherProvider", FingerprintReferences.SubjectReference(s.Reader.Id, 56)));

            Assert.Equal(new[] { 0, 1, 7, 22 }, await InventoryAsync(s.ReaderClient));
            Assert.Equal(new[] { 3, 7 }, await InventoryAsync(s.Host.Reader(other)));

            // Revoked with its deletion Pending: still occupied, also after a sensor error.
            var seventhDeletion = await RevokeAsync(s, seventh, seventhReference);
            Assert.Equal(new[] { 0, 1, 7, 22 }, await InventoryAsync(s.ReaderClient));
            Assert.Equal(HttpStatusCode.NoContent, (await ReportDeletionAsync(s, seventhDeletion, "SensorError")).StatusCode);
            Assert.Equal(new[] { 0, 1, 7, 22 }, await InventoryAsync(s.ReaderClient));

            // An active reference and a pending deletion on the same slot (legacy data): the slot appears once.
            var legacy = await s.Host.Web.SeedPatientAsync("Legado");
            var sameSlot = PatientBiometricReference.Register(legacy.Id, BiometricModality.Fingerprint, FingerprintReferences.Provider, FingerprintReferences.SubjectReference(s.Reader.Id, 7));
            await AddReferencesAsync(s, sameSlot);
            Assert.Equal(new[] { 0, 1, 7, 22 }, await InventoryAsync(s.ReaderClient));
            await s.Host.Web.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE clinical."PatientBiometricReferences" SET "Status" = 2, "RevokedAtUtc" = GREATEST(now(), "CreatedAtUtc") WHERE "Id" = {sameSlot.Id}"""));

            // Completed (Deleted or NotFound): no longer occupied.
            Assert.Equal(HttpStatusCode.NoContent, (await ReportDeletionAsync(s, seventhDeletion, "Deleted")).StatusCode);
            Assert.Equal(new[] { 0, 1, 22 }, await InventoryAsync(s.ReaderClient));
            var firstDeletion = await RevokeAsync(s, first, firstReference);
            Assert.Equal(new[] { 0, 1, 22 }, await InventoryAsync(s.ReaderClient));
            Assert.Equal(HttpStatusCode.NoContent, (await ReportDeletionAsync(s, firstDeletion, "NotFound")).StatusCode);
            Assert.Equal(new[] { 0, 22 }, await InventoryAsync(s.ReaderClient));
            Assert.Equal(new[] { 3, 7 }, await InventoryAsync(s.Host.Reader(other)));
        }

        // References that do not follow the canonical format exactly (active ones and one with a Pending deletion job) are
        // left out: no 500, never reinterpreted, never echoed, only counted in the log.
        [SkippableFact]
        public async Task MalformedReferencesAreIgnoredWithoutFailingOrLeaking()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;
            await EnrollAsync(s, 4);
            var patient = await s.Host.Web.SeedPatientAsync("Rosa");
            var prefix = FingerprintReferences.ReaderPrefix(s.Reader.Id);
            var malformed = new[] { $"{prefix}0008", $"{prefix}+9", $"{prefix}-10", $"{prefix}1 1", $"{prefix}abc", $"{prefix}", $"{prefix}12:slot:13", $"{prefix}99999999999" }
                .Select(subject => PatientBiometricReference.Register(patient.Id, BiometricModality.Fingerprint, FingerprintReferences.Provider, subject))
                .ToArray();
            var withJob = PatientBiometricReference.Register(patient.Id, BiometricModality.Fingerprint, FingerprintReferences.Provider, $"{prefix}0014");
            await AddReferencesAsync(s, [.. malformed, withJob]);
            await s.Host.Web.DbAsync(async db =>
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE clinical."PatientBiometricReferences" SET "Status" = 2, "RevokedAtUtc" = GREATEST(now(), "CreatedAtUtc") WHERE "Id" = {withJob.Id}""");
                return await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""INSERT INTO clinical."FingerprintSlotDeletionJobs" ("Id", "BiometricReferenceId", "DeviceCredentialId", "Status", "Attempts", "CreatedAtUtc") VALUES ({Guid.NewGuid()}, {withJob.Id}, {s.Reader.Id}, 1, 0, now())""");
            });

            var response = await s.ReaderClient.GetAsync(Slots);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var raw = await response.Content.ReadAsStringAsync();
            Assert.Equal("""{"slots":[4]}""", raw);
            Assert.Contains("fingerprint-slot-inventory", s.Host.Logs.Text);
            Assert.Contains("9 unreadable references ignored", s.Host.Logs.Text);
            foreach (var secret in new[] { "r307", "99999999999", "12:slot:13" })
            {
                Assert.DoesNotContain(secret, s.Host.Logs.Text, StringComparison.OrdinalIgnoreCase);
            }
        }

        // DeviceApiKey over HTTPS, Device.Fingerprint.Enroll, the reader only from its credential: no parameter selects
        // another reader's inventory.
        [SkippableFact]
        public async Task OnlyAnAuthenticatedEnrollReaderGetsItsOwnInventory()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;
            await EnrollAsync(s, 5);
            var other = await s.Host.ProvisionReaderAsync(DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            await EnrollAsync(s, 6, other);
            var identifyOnly = await s.Host.ProvisionReaderAsync(DeviceScopes.FingerprintIdentify);
            var heartbeatOnly = await s.Host.ProvisionReaderAsync(DeviceScopes.Heartbeat);
            var revoked = await s.Host.ProvisionReaderAsync(DeviceScopes.FingerprintEnroll, DeviceScopes.FingerprintIdentify);
            await s.Host.RevokeReaderAsync(revoked.Id);

            foreach (var client in new[] { s.Host.DeviceAnonymous(), s.Host.Reader(s.Reader.Code, "wrong-key"), s.Host.Reader("UNKNOWN-READER", s.Reader.Key),
                s.Host.Reader(revoked), s.Host.ReaderOverHttp(s.Reader) })
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Slots)).StatusCode);
            }

            foreach (var reader in new[] { identifyOnly, heartbeatOnly })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await s.Host.Reader(reader).GetAsync(Slots)).StatusCode);
            }

            var query = $"{Slots}?deviceId={other.Id}&deviceCredentialId={other.Id}&readerId={other.Id}&patientId={Guid.NewGuid()}";
            var asOther = await s.ReaderClient.GetAsync(query);
            Assert.Equal((HttpStatusCode.OK, """{"slots":[5]}"""), (asOther.StatusCode, await asOther.Content.ReadAsStringAsync()));
            Assert.Equal(new[] { 6 }, await InventoryAsync(s.Host.Reader(other)));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await PostJsonAsync(s.ReaderClient, Slots, "{}")).StatusCode);
        }

        // Only {"slots":[...]} with no-store; nothing about patients, references, subject references, jobs or the reader in
        // the body; the count, never a slot, in the logs; the Device rate limit policy.
        [SkippableFact]
        public async Task TheResponseCarriesOnlySlotNumbersAndTheLogsNone()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await SetupAsync();
            await using var _host = s.Host;
            var (activePatient, activeReference) = await EnrollAsync(s, 6143);
            var (revokedPatient, revokedReference) = await EnrollAsync(s, 7919);
            var deletionId = await RevokeAsync(s, revokedPatient, revokedReference);

            var response = await s.ReaderClient.GetAsync(Slots);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
            var raw = await response.Content.ReadAsStringAsync();
            Assert.Equal("""{"slots":[6143,7919]}""", raw);
            foreach (var secret in new[]
            {
                activePatient.ToString(), revokedPatient.ToString(), activeReference.ToString(), revokedReference.ToString(), deletionId.ToString(),
                s.Reader.Id.ToString(), s.Reader.Code, "r307", "subjectReference", "patient", "deletion", "status", "Paciente"
            })
            {
                Assert.DoesNotContain(secret, raw, StringComparison.OrdinalIgnoreCase);
            }

            var logs = s.Host.Logs.TextWithoutCorrelationIds;
            Assert.Contains("Device fingerprint-slot-inventory", logs);
            Assert.Contains("2 slots", logs);
            foreach (var secret in new[] { "6143", "7919", "r307:device", s.Reader.Key, activePatient.ToString(), revokedPatient.ToString() })
            {
                Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
            }

            // A read like the other polls of the reader: no clinical audit entry.
            var audits = await s.Host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync());
            Assert.Equal(HttpStatusCode.OK, (await s.ReaderClient.GetAsync(Slots)).StatusCode);
            Assert.Equal(audits, await s.Host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync()));
        }
    }

    public sealed class LowRateLimitDeviceFactory : BffFactory<HeartbeatController>
    {
        protected override IDictionary<string, string?> Settings =>
            TestSettings.Create(new Dictionary<string, string?> { ["RateLimiting:DevicePermitLimit"] = "2" });
    }

    // The inventory shares the Device API's per-reader rate limit (no database needed: in-memory stores).
    public sealed class FingerprintSlotInventoryRateLimitTests
    {
        [Fact]
        public async Task TheInventoryIsRateLimitedPerReader()
        {
            await using var factory = new LowRateLimitDeviceFactory();
            await using var scope = factory.Services.CreateAsyncScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecureTokenGenerator>();
            var code = $"DEV-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
            var key = secrets.Generate(32);
            await scope.ServiceProvider.GetRequiredService<IDeviceCredentialStore>().AddAsync(
                new NewDeviceCredential(code, "Test reader", [DeviceScopes.FingerprintEnroll], secrets.Hash(key), null), CancellationToken.None);
            var client = factory.CreateHttpsClient();
            client.DefaultRequestHeaders.Add(DeviceAuthenticationDefaults.DeviceIdHeader, code);
            client.DefaultRequestHeaders.Add(DeviceAuthenticationDefaults.DeviceKeyHeader, key);

            var allowed = new[] { await client.GetAsync("/api/device/fingerprint/slots"), await client.GetAsync("/api/device/fingerprint/slots") };
            var limited = await client.GetAsync("/api/device/fingerprint/slots");

            Assert.All(allowed, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            Assert.Equal("""{"slots":[]}""", await allowed[0].Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.True(limited.Headers.RetryAfter is not null);
        }
    }
}
