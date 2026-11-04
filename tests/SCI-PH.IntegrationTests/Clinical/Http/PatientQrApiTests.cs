using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
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
    // The patient QR (ADR-015) through the real Mobile and Web BFFs over the migrated PostgreSQL schema: unique indexes,
    // check constraints, composite foreign keys and transactions behave as in production. The expiry uses a controlled clock.
    [Collection(PostgresCollection.Name)]
    public sealed class PatientQrApiTests
    {
        private const string Qr = "/api/mobile/patient/me/qr";
        private const string Emergencies = "/api/mobile/emergencies";
        private readonly PostgresFixture _postgres;

        public PatientQrApiTests(PostgresFixture postgres) => _postgres = postgres;

        private sealed record Credential(string Payload, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc);

        // To the millisecond: what the API and PostgreSQL keep of a time.
        private static FakeTimeProvider Clock()
        {
            var now = DateTimeOffset.UtcNow;
            return new FakeTimeProvider(now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond)));
        }

        private static string Resolve(Guid emergencyId) => $"{Emergencies}/{emergencyId}/identify/qr/resolve";

        private static string Confirm(Guid emergencyId) => $"{Emergencies}/{emergencyId}/identify/qr/confirm";

        private static async Task<Credential> IssueAsync(HttpClient patient)
        {
            var response = await patient.PostAsync(Qr, null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<Credential>())!;
        }

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

        private static Task<HttpResponseMessage> ResolveAsync(StaffSession staff, Guid emergencyId, string payload) =>
            staff.Client.PostAsJsonAsync(Resolve(emergencyId), new { payload });

        private static Task<HttpResponseMessage> ConfirmAsync(StaffSession staff, Guid emergencyId, string payload, Guid? clientRequestId = null) =>
            staff.Client.PostAsJsonAsync(Confirm(emergencyId), new { clientRequestId = clientRequestId ?? Guid.NewGuid(), payload });

        private static async Task<StaffSession> ParamedicAsync(MobileEmergencyHost host) =>
            await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);

        private static string HashOf(string payload)
        {
            Assert.True(PatientQrToken.TryHash(payload, out var hash));
            return hash;
        }

        private static Task<List<PatientIdentifier>> QrRowsAsync(MobileEmergencyHost host, Guid patientId) =>
            host.Web.DbAsync(db => db.PatientIdentifiers.AsNoTracking()
                .Where(i => i.PatientId == patientId && i.Type == PatientIdentifierType.QrCode).ToListAsync());

        private static Task<List<IdentificationAttempt>> SuccessfulAttemptsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.IdentificationAttempts.AsNoTracking()
                .Where(a => a.ClinicalEncounterId == emergencyId && a.Outcome == IdentificationOutcome.Success).ToListAsync());

        private static async Task AssertNoStoredSecretAsync(MobileEmergencyHost host, string payload)
        {
            var token = payload[PatientQrToken.Prefix.Length..];
            var hash = HashOf(payload);
            foreach (var entry in await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking().ToListAsync()))
            {
                foreach (var text in new[] { entry.EntityId, entry.EntityName, entry.ChangesJson, entry.FailureReason })
                {
                    Assert.DoesNotContain(token, text ?? string.Empty);
                    Assert.DoesNotContain(hash, text ?? string.Empty);
                    Assert.DoesNotContain(hash[PatientQrToken.HashPrefix.Length..], text ?? string.Empty);
                }
            }

            foreach (var attempt in await host.Web.DbAsync(db => db.IdentificationAttempts.AsNoTracking().ToListAsync()))
            {
                Assert.DoesNotContain(token, $"{attempt.DeviceReference}{attempt.Provider}{attempt.ProviderRequestReference}");
                Assert.DoesNotContain(hash, $"{attempt.DeviceReference}{attempt.Provider}{attempt.ProviderRequestReference}");
            }

            foreach (var logs in new[] { host.Logs.Text, host.Web.Logs.Text })
            {
                Assert.DoesNotContain(token, logs);
                Assert.DoesNotContain(hash, logs);
                Assert.DoesNotContain(hash[PatientQrToken.HashPrefix.Length..], logs);
            }
        }

        // ---- Issuing ----

        [SkippableFact]
        public async Task IssuingReturnsTheOpaquePayloadOnceAndStoresOnlyItsHash()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var clock = Clock();
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, mobileServices: s => s.AddSingleton<TimeProvider>(clock));
            var (record, client) = await host.SignInPatientAsync("Alba");

            var response = await client.PostAsync(Qr, null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var raw = await response.Content.ReadAsStringAsync();
            using (var json = JsonDocument.Parse(raw))
            {
                Assert.Equal(["expiresAtUtc", "issuedAtUtc", "payload"], json.RootElement.EnumerateObject().Select(p => p.Name).Order());
            }

            var credential = JsonSerializer.Deserialize<Credential>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Matches("^sciphqr:v1:[A-Za-z0-9_-]{43}$", credential.Payload);
            Assert.Equal(TimeSpan.FromMinutes(15), credential.ExpiresAtUtc - credential.IssuedAtUtc);
            Assert.Equal(clock.GetUtcNow(), credential.IssuedAtUtc);
            // Exactly 32 random bytes, and the payload says nothing about the patient.
            Assert.Equal(32, System.Buffers.Text.Base64Url.DecodeFromChars(credential.Payload.AsSpan(PatientQrToken.Prefix.Length)).Length);
            foreach (var hidden in new[] { record.PatientId.ToString(), record.PatientId.ToString("N"), record.GivenNames })
            {
                Assert.DoesNotContain(hidden, raw, StringComparison.OrdinalIgnoreCase);
            }

            var row = Assert.Single(await QrRowsAsync(host, record.PatientId));
            var token = System.Buffers.Text.Base64Url.DecodeFromChars(credential.Payload.AsSpan(PatientQrToken.Prefix.Length));
            var expected = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(token));
            Assert.Equal((expected, expected), (row.Value, row.NormalizedValue));
            Assert.Equal(
                (PatientIdentifierSource.System, PatientIdentifierStatus.Active, true, string.Empty),
                (row.Source, row.Status, row.IsPrimary, row.Issuer));
            Assert.Equal(credential.ExpiresAtUtc, row.ValidUntilUtc);
            await AssertNoStoredSecretAsync(host, credential.Payload);

            // The token is in no column of any row of the identifiers table, and no image is ever stored.
            var plain = credential.Payload[PatientQrToken.Prefix.Length..];
            var plaintextRows = await host.Web.DbAsync(db => db.PatientIdentifiers.CountAsync(i => i.Value.Contains(plain) || i.NormalizedValue.Contains(plain)));
            Assert.Equal(0, plaintextRows);
        }

        [SkippableFact]
        public async Task ReissuingRevokesThePreviousCredentialAtomically()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (record, client) = await host.SignInPatientAsync("Alba");
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);

            var first = await IssueAsync(client);
            var second = await IssueAsync(client);

            Assert.NotEqual(first.Payload, second.Payload);
            var rows = await QrRowsAsync(host, record.PatientId);
            Assert.Equal(2, rows.Count);
            var active = Assert.Single(rows, r => r.Status == PatientIdentifierStatus.Active);
            Assert.Equal(HashOf(second.Payload), active.NormalizedValue);
            var revoked = Assert.Single(rows, r => r.Status == PatientIdentifierStatus.Revoked);
            Assert.False(revoked.IsPrimary);
            Assert.NotNull(revoked.RevokedAtUtc);

            Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync(paramedic, emergency.Id, first.Payload)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(paramedic, emergency.Id, second.Payload)).StatusCode);

            var audit = await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .Where(a => a.EntityName == PatientQrService.AuditResource).ToListAsync());
            Assert.Equal(2, audit.Count(a => a.Action == AuditActionType.Create));
            Assert.Equal(1, audit.Count(a => a.Action == AuditActionType.Delete));
            Assert.All(audit, a => Assert.Equal((AuditOutcome.Success, (string?)null, record.PatientId), (a.Outcome, a.ChangesJson, a.PatientId!.Value)));
            await AssertNoStoredSecretAsync(host, first.Payload);
            await AssertNoStoredSecretAsync(host, second.Payload);
        }

        [SkippableFact]
        public async Task ConcurrentReissuesLeaveExactlyOneActiveCredential()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (record, client) = await host.SignInPatientAsync("Alba");
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);

            // With and without a previous credential.
            for (var round = 0; round < 2; round++)
            {
                var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsync(Qr, null)));

                Assert.All(responses, r => Assert.True(
                    r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"Unexpected {r.StatusCode}"));
                var issued = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
                Assert.NotEmpty(issued);

                var active = Assert.Single(await QrRowsAsync(host, record.PatientId), r => r.Status == PatientIdentifierStatus.Active);

                // Whoever got a token: only the one committed last is valid. No two valid tokens remain.
                var valid = 0;
                foreach (var response in issued)
                {
                    var credential = (await response.Content.ReadFromJsonAsync<Credential>())!;
                    if ((await ResolveAsync(paramedic, emergency.Id, credential.Payload)).StatusCode == HttpStatusCode.OK)
                    {
                        valid++;
                        Assert.Equal(active.NormalizedValue, HashOf(credential.Payload));
                    }
                }

                Assert.Equal(1, valid);
            }

            // A loser that retries once converges to a new single credential.
            var retried = await IssueAsync(client);
            var final = Assert.Single(await QrRowsAsync(host, record.PatientId), r => r.Status == PatientIdentifierStatus.Active);
            Assert.Equal(HashOf(retried.Payload), final.NormalizedValue);
        }

        [SkippableFact]
        public async Task RevokingIsIdempotentAndEndsTheCredential()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (record, client) = await host.SignInPatientAsync("Alba");
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Qr)).StatusCode);
            Assert.Empty(await QrRowsAsync(host, record.PatientId));

            var credential = await IssueAsync(client);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Qr)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Qr)).StatusCode);

            Assert.Equal(PatientIdentifierStatus.Revoked, Assert.Single(await QrRowsAsync(host, record.PatientId)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync(paramedic, emergency.Id, credential.Payload)).StatusCode);
            Assert.Single(await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .Where(a => a.EntityName == PatientQrService.AuditResource && a.Action == AuditActionType.Delete).ToListAsync()));
        }

        [SkippableFact]
        public async Task OnlyALinkedPatientAccountHasACredentialAndItIsAlwaysItsOwn()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (a, clientA) = await host.SignInPatientAsync("Alba");
            var (b, _) = await host.SignInPatientAsync("Bruno");
            var paramedic = await ParamedicAsync(host);
            var unlinked = await host.LoginAsync(await host.Web.Factory.CreateUserAsync(SecurityRoles.Patient));

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Anonymous().PostAsync(Qr, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await paramedic.Client.PostAsync(Qr, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await unlinked.PostAsync(Qr, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await unlinked.DeleteAsync(Qr)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await clientA.GetAsync(Qr)).StatusCode);

            // A patient id sent by the client means nothing: the credential is of the signed-in patient.
            var response = await clientA.PostAsJsonAsync($"{Qr}?patientId={b.PatientId}", new { patientId = b.PatientId });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(await QrRowsAsync(host, b.PatientId));
            Assert.Single(await QrRowsAsync(host, a.PatientId));
        }

        [SkippableFact]
        public async Task ADisabledAccountCannotIssueButItsCredentialIdentifiesUntilItExpires()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var clock = Clock();
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, mobileServices: s => s.AddSingleton<TimeProvider>(clock));
            var (record, client, userId) = await host.SignInPatientAccountAsync("Alba");
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            var credential = await IssueAsync(client);

            await using (var scope = host.MobileServices.CreateAsyncScope())
            {
                Assert.True(await scope.ServiceProvider.GetRequiredService<IIdentityService>().SetActiveAsync(userId, false, CancellationToken.None));
            }

            // The access token is still valid (ADR-006); the account check is what stops a new credential.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/mobile/patient/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(Qr, null)).StatusCode);

            var row = Assert.Single(await QrRowsAsync(host, record.PatientId));
            Assert.Equal((PatientIdentifierStatus.Active, HashOf(credential.Payload)), (row.Status, row.NormalizedValue));
            var denied = Assert.Single(await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .Where(a => a.EntityName == PatientQrService.AuditResource && a.Outcome == AuditOutcome.Failure).ToListAsync()));
            Assert.Equal("AccountInactive", denied.FailureReason);

            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(paramedic, emergency.Id, credential.Payload)).StatusCode);
            clock.Advance(TimeSpan.FromMinutes(15));
            Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync(paramedic, emergency.Id, credential.Payload)).StatusCode);
        }

        // ---- Invalid QR ----

        [SkippableFact]
        public async Task TheCredentialExpiresAtFifteenMinutesOnTheServerClock()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var clock = Clock();
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, mobileServices: s => s.AddSingleton<TimeProvider>(clock));
            var (record, client) = await host.SignInPatientAsync("Alba");
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            var credential = await IssueAsync(client);

            clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(paramedic, emergency.Id, credential.Payload)).StatusCode);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync(paramedic, emergency.Id, credential.Payload)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ConfirmAsync(paramedic, emergency.Id, credential.Payload)).StatusCode);

            // Expired, it stays Active until replaced; a new one replaces it at a later server time.
            Assert.Equal(PatientIdentifierStatus.Active, Assert.Single(await QrRowsAsync(host, record.PatientId)).Status);
            var renewed = await IssueAsync(client);
            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(paramedic, emergency.Id, renewed.Payload)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync(paramedic, emergency.Id, credential.Payload)).StatusCode);
        }

        [SkippableFact]
        public async Task EveryInvalidQrIsTheSameNotFoundAndRecordedAsASafeAttempt()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (_, client) = await host.SignInPatientAsync("Alba");
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            var revoked = await IssueAsync(client);
            var current = await IssueAsync(client);
            var token = current.Payload[PatientQrToken.Prefix.Length..];
            var flipped = (token[0] == 'A' ? 'B' : 'A') + token[1..];

            string[] noMatch =
            [
                PatientQrToken.Generate().Payload,                               // random, never issued
                PatientQrToken.Prefix + flipped,                                 // modified
                "sciphqr:v2:" + token,                                           // unknown version
                "SCIPHQR:V1:" + token,                                           // not the canonical prefix
                token,                                                           // no prefix
                PatientQrToken.Prefix + token + "=",                             // padding
                " " + current.Payload,                                           // spaces
                PatientQrToken.Prefix + token[..^1] + "!",                       // not base64url
                PatientQrToken.Prefix + token[..^1],                             // too short
                "",                                                              // empty
                HashOf(current.Payload)                                          // the stored hash is not a QR
            ];

            var bodies = new List<string>();
            foreach (var payload in noMatch.Append(revoked.Payload))
            {
                var response = await ResolveAsync(paramedic, emergency.Id, payload);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                bodies.Add($"{problem.RootElement.GetProperty("title").GetString()}|{problem.RootElement.GetProperty("detail").GetString()}");
            }

            Assert.Single(bodies.Distinct());

            var attempts = await host.Web.DbAsync(db => db.IdentificationAttempts.AsNoTracking()
                .Where(a => a.ClinicalEncounterId == emergency.Id).ToListAsync());
            Assert.Equal(noMatch.Length + 1, attempts.Count);
            Assert.All(attempts, a =>
            {
                Assert.Equal((IdentificationMethod.QrCode, IdentificationOutcome.Failure, (Guid?)null, (Guid?)null), (a.Method, a.Outcome, a.PatientId, a.PatientIdentifierId));
                Assert.Equal((paramedic.UserId, paramedic.StaffId), (a.ActorIdentityUserId!.Value, a.PerformedByStaffId));
            });
            Assert.Equal(noMatch.Length, attempts.Count(a => a.FailureReason == IdentificationFailureReason.NoMatch));
            Assert.Equal(1, attempts.Count(a => a.FailureReason == IdentificationFailureReason.InvalidCredential));

            // The current QR still works, and the emergency stayed NN throughout.
            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(paramedic, emergency.Id, current.Payload)).StatusCode);
            Assert.Null((await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == emergency.Id))).PatientId);
            await AssertNoStoredSecretAsync(host, current.Payload);
        }

        // ---- Reserved for the system ----

        [SkippableFact]
        public async Task StaffCannotRegisterOrSearchAQrIdentifier()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (_, client) = await host.SignInPatientAsync("Alba");
            var credential = await IssueAsync(client);
            var receptionist = await host.Web.SignInAsync(SecurityRoles.Receptionist, [await host.Web.CreateClinicAsync()]);
            var patientsBefore = await host.Web.DbAsync(db => db.Patients.CountAsync());

            foreach (var value in new[] { "QR-123", credential.Payload, HashOf(PatientQrToken.Generate().Payload) })
            {
                var registration = await receptionist.Client.PostAsJsonAsync("/api/web/patients", new
                {
                    givenNames = "Falsa",
                    familyNames = "Persona",
                    sex = "Female",
                    identifier = new { type = "QrCode", value }
                });
                Assert.Equal(HttpStatusCode.BadRequest, registration.StatusCode);
                Assert.DoesNotContain(value, await registration.Content.ReadAsStringAsync());

                var search = await receptionist.Client.PostAsJsonAsync("/api/web/patients/search", new { identifierType = "QrCode", value });
                Assert.Equal(HttpStatusCode.BadRequest, search.StatusCode);
                Assert.DoesNotContain(value, await search.Content.ReadAsStringAsync());
            }

            Assert.Equal(patientsBefore, await host.Web.DbAsync(db => db.Patients.CountAsync()));
        }

        [SkippableFact]
        public async Task AQrIdentifierNotIssuedByTheSystemNeverResolves()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            var (payload, hash) = PatientQrToken.Generate();
            var planted = Patient.Register("Plantado", "Manual", null, BiologicalSex.Female);
            planted.AddIdentifier(PatientIdentifierType.QrCode, hash, PatientIdentifierSource.Manual, isPrimary: true);
            await host.Web.DbAsync(async db =>
            {
                db.Patients.Add(planted);
                await db.SaveChangesAsync();
            });

            Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync(paramedic, emergency.Id, payload)).StatusCode);
        }

        [SkippableFact]
        public async Task TheHashNeverAppearsInAPatientSummaryEvenAsTheOnlyIdentifier()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var account = await host.Web.Factory.CreateUserAsync(SecurityRoles.Patient);
            var only = Patient.Register("Solamente", "Qrperez", new DateOnly(1990, 1, 2), BiologicalSex.Female);
            only.LinkIdentityUser(account.Id);
            await host.Web.DbAsync(async db =>
            {
                db.Patients.Add(only);
                await db.SaveChangesAsync();
            });
            var patient = await host.LoginAsync(account);
            var credential = await IssueAsync(patient);
            var hash = HashOf(credential.Payload);
            var receptionist = await host.Web.SignInAsync(SecurityRoles.Receptionist, [await host.Web.CreateClinicAsync()]);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);

            var byName = await receptionist.Client.PostAsJsonAsync("/api/web/patients/search", new { value = "Solamente" });
            var byId = await receptionist.Client.GetAsync($"/api/web/patients/{only.Id}");
            var own = await patient.GetAsync("/api/mobile/patient/me");
            var candidate = await ResolveAsync(paramedic, emergency.Id, credential.Payload);
            var confirmed = await ConfirmAsync(paramedic, emergency.Id, credential.Payload);

            foreach (var response in new[] { byName, byId, own, candidate, confirmed })
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain("QrCode", body);
                Assert.DoesNotContain(hash[^4..], body);
                Assert.DoesNotContain(hash, body);
                Assert.DoesNotContain(credential.Payload, body);
            }

            var summary = await byId.ReadAsAsync<PatientSummary>();
            Assert.Null(summary.PrimaryIdentifier);
            Assert.Null((await candidate.ReadAsAsync<PatientQrCandidate>()).Document);
            Assert.Null((await confirmed.ReadAsAsync<PrehospitalEmergencySummary>()).Patient!.PrimaryIdentifier);
        }

        // ---- Staff ----

        [SkippableFact]
        public async Task OnlyAuthorizedActiveStaffWithAccessToTheEmergencyCanUseAQr()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (_, patient) = await host.SignInPatientAsync("Alba");
            var credential = await IssueAsync(patient);
            var clinic = await host.Web.CreateClinicAsync();
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var emergency = await OpenAsync(paramedic);
            var denied = new Dictionary<string, HttpClient>
            {
                ["paramedic outside the emergency"] = (await host.SignInAsync(SecurityRoles.Paramedic, [clinic])).Client,
                ["inactive staff"] = (await host.SignInAsync(SecurityRoles.Paramedic, [clinic], staffActive: false)).Client,
                ["paramedic without staff record"] = (await host.SignInAsync(SecurityRoles.Paramedic, [clinic], asClinicalStaff: false)).Client,
                ["administrator without staff record"] = (await host.SignInAsync(SecurityRoles.Administrator, [clinic], asClinicalStaff: false)).Client,
                ["nurse lacks the permissions"] = (await host.SignInAsync(SecurityRoles.Nurse, [clinic], staffType: ClinicalStaffType.Nurse)).Client,
                ["receptionist lacks Emergencies.Operate"] = (await host.SignInAsync(SecurityRoles.Receptionist, [clinic], staffType: ClinicalStaffType.OtherClinical)).Client,
                ["doctor lacks Emergencies.Operate"] = (await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician)).Client,
                ["the patient"] = patient
            };

            foreach (var (who, client) in denied)
            {
                foreach (var response in new[]
                {
                    await client.PostAsJsonAsync(Resolve(emergency.Id), new { payload = credential.Payload }),
                    await client.PostAsJsonAsync(Confirm(emergency.Id), new { clientRequestId = Guid.NewGuid(), payload = credential.Payload })
                })
                {
                    Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{who}: {response.StatusCode}");
                    var body = await response.Content.ReadAsStringAsync();
                    Assert.DoesNotContain("Alba", body);
                }
            }

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Anonymous().PostAsJsonAsync(Resolve(emergency.Id), new { payload = credential.Payload })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync(paramedic, Guid.NewGuid(), credential.Payload)).StatusCode);

            // Nothing was learned or recorded about the patient: access is checked before the QR is looked at.
            Assert.Equal(0, await host.Web.DbAsync(db => db.IdentificationAttempts.CountAsync()));
            Assert.Equal(0, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.EntityName == EmergencyQrIdentificationService.CandidateAuditResource)));
            Assert.Null((await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == emergency.Id))).PatientId);
        }

        [SkippableFact]
        public async Task TheQrNeverTravelsInTheRouteOrAcceptsExtraFields()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (record, patient) = await host.SignInPatientAsync("Alba");
            var credential = await IssueAsync(patient);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await paramedic.Client.PostAsync($"{Resolve(emergency.Id)}?payload={Uri.EscapeDataString(credential.Payload)}", null)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await paramedic.Client.PostAsJsonAsync(Resolve(emergency.Id), new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await paramedic.Client.PostAsJsonAsync(Resolve(emergency.Id), new { payload = credential.Payload, patientId = record.PatientId })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await paramedic.Client.PostAsJsonAsync(Confirm(emergency.Id), new { payload = credential.Payload })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await paramedic.Client.PostAsJsonAsync(Confirm(emergency.Id), new { clientRequestId = Guid.Empty, payload = credential.Payload })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await paramedic.Client.PostAsJsonAsync(Confirm(emergency.Id), new { clientRequestId = Guid.NewGuid(), payload = credential.Payload, patientId = record.PatientId })).StatusCode);
            Assert.DoesNotContain(credential.Payload, host.Logs.Text);
        }

        // ---- The emergency ----

        [SkippableFact]
        public async Task ResolvingShowsACandidateAndChangesNothing()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (record, patient) = await host.SignInPatientAsync("Alba");
            var credential = await IssueAsync(patient);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);

            var response = await ResolveAsync(paramedic, emergency.Id, credential.Payload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var raw = await response.Content.ReadAsStringAsync();
            using (var json = JsonDocument.Parse(raw))
            {
                // Only what a human needs to confirm.
                Assert.Equal(["dateOfBirth", "displayName", "document"], json.RootElement.EnumerateObject().Select(p => p.Name).Order());
                Assert.Equal(["type", "value"], json.RootElement.GetProperty("document").EnumerateObject().Select(p => p.Name).Order());
                Assert.Equal("NationalId", json.RootElement.GetProperty("document").GetProperty("type").GetString());
                Assert.StartsWith("***", json.RootElement.GetProperty("document").GetProperty("value").GetString());
                Assert.Equal($"{record.GivenNames} Prueba", json.RootElement.GetProperty("displayName").GetString());
                Assert.Equal("1990-05-20", json.RootElement.GetProperty("dateOfBirth").GetString());
            }

            foreach (var hidden in new[] { record.PatientId.ToString(), credential.Payload, HashOf(credential.Payload), PatientRecordSeed.ReasonMarker, "Penicilina", "Amoxicilina" })
            {
                Assert.DoesNotContain(hidden, raw);
            }

            var encounter = await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == emergency.Id));
            Assert.Null(encounter.PatientId);
            Assert.Null(encounter.IdentifiedAtUtc);
            Assert.Equal(emergency.TemporaryPatientCode, encounter.TemporaryPatientCode!.Value);
            Assert.Equal(0, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergency.Id && a.Action == AuditActionType.Identify)));

            var audit = await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .SingleAsync(a => a.EntityName == EmergencyQrIdentificationService.CandidateAuditResource));
            Assert.Equal(
                (AuditActionType.Read, AuditOutcome.Success, (Guid?)record.PatientId, (Guid?)emergency.Id, (Guid?)paramedic.UserId, paramedic.StaffId, (string?)null),
                (audit.Action, audit.Outcome, audit.PatientId, audit.ClinicalEncounterId, audit.ActorIdentityUserId, audit.ActorStaffId, audit.ChangesJson));

            // Candidate discovery is not an identification: no attempt at all, however many times it is resolved.
            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(paramedic, emergency.Id, credential.Payload)).StatusCode);
            Assert.Equal(0, await host.Web.DbAsync(db => db.IdentificationAttempts.CountAsync()));
            await AssertNoStoredSecretAsync(host, credential.Payload);
        }

        [SkippableFact]
        public async Task ConfirmingIdentifiesTheSameEmergencyThroughTheExistingUseCase()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var clock = Clock();
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, mobileServices: s => s.AddSingleton<TimeProvider>(clock));
            var (record, patient) = await host.SignInPatientAsync("Alba");
            var credential = await IssueAsync(patient);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            var encountersBefore = await host.Web.DbAsync(db => db.ClinicalEncounters.CountAsync());
            var key = Guid.NewGuid();

            var response = await ConfirmAsync(paramedic, emergency.Id, credential.Payload, key);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var identified = await response.ReadAsAsync<PrehospitalEmergencySummary>();
            Assert.Equal((emergency.Id, emergency.TemporaryPatientCode, record.PatientId), (identified.Id, identified.TemporaryPatientCode, identified.Patient!.Id));
            Assert.Equal(encountersBefore, await host.Web.DbAsync(db => db.ClinicalEncounters.CountAsync()));
            var encounter = await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == emergency.Id));
            Assert.Equal((record.PatientId, paramedic.StaffId, ClinicalEncounterType.Prehospital), (encounter.PatientId!.Value, encounter.IdentifiedByStaffId, encounter.Type));
            Assert.Equal(emergency.TemporaryPatientCode, encounter.TemporaryPatientCode!.Value);

            var identifyAudit = await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .SingleAsync(a => a.ClinicalEncounterId == emergency.Id && a.Action == AuditActionType.Identify));
            Assert.Equal((record.PatientId, paramedic.UserId, paramedic.StaffId), (identifyAudit.PatientId!.Value, identifyAudit.ActorIdentityUserId!.Value, identifyAudit.ActorStaffId));
            Assert.DoesNotContain(credential.Payload, identifyAudit.ChangesJson);

            var receipt = await host.Web.DbAsync(db => db.IdempotencyReceipts.AsNoTracking().SingleAsync(r => r.ClientRequestId == key));
            Assert.Equal((EmergencyQrIdentificationService.ConfirmOperation, paramedic.UserId, emergency.Id), (receipt.Operation, receipt.ActorUserId, receipt.ResourceId));
            Assert.Matches("^[0-9a-f]{64}$", receipt.RequestHash);
            Assert.NotEqual(HashOf(credential.Payload)[PatientQrToken.HashPrefix.Length..], receipt.RequestHash);
            // Confirmed directly, with no previous resolve.
            var row = Assert.Single(await QrRowsAsync(host, record.PatientId));
            var attempt = Assert.Single(await SuccessfulAttemptsAsync(host, emergency.Id));
            Assert.Equal(
                (IdentificationMethod.QrCode, (Guid?)record.PatientId, (Guid?)row.Id, (Guid?)paramedic.UserId, paramedic.StaffId),
                (attempt.Method, attempt.PatientId, attempt.PatientIdentifierId, attempt.ActorIdentityUserId, attempt.PerformedByStaffId));

            // A replay is the same result, with nothing new, also once the QR has expired or been replaced.
            await IssueAsync(patient);
            clock.Advance(TimeSpan.FromMinutes(20));
            var replay = await ConfirmAsync(paramedic, emergency.Id, credential.Payload, key);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(record.PatientId, (await replay.ReadAsAsync<PrehospitalEmergencySummary>()).Patient!.Id);
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergency.Id && a.Action == AuditActionType.Identify)));
            Assert.Equal(1, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.ClientRequestId == key)));
            Assert.Single(await SuccessfulAttemptsAsync(host, emergency.Id));

            // The same key with another QR, and any other identification of it, is a controlled conflict.
            var other = PatientQrToken.Generate().Payload;
            Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(paramedic, emergency.Id, other, key)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(paramedic, emergency.Id, credential.Payload)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await ResolveAsync(paramedic, emergency.Id, credential.Payload)).StatusCode);

            // The identified emergency goes on as any other.
            Assert.Equal(HttpStatusCode.OK, (await paramedic.Client.GetAsync($"{Emergencies}/{emergency.Id}")).StatusCode);
            var vitals = await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergency.Id}/vital-signs", new
            {
                clientRequestId = Guid.NewGuid(),
                recordedAt = DateTimeOffset.UtcNow,
                heartRate = 80
            });
            Assert.Equal(HttpStatusCode.Created, vitals.StatusCode);
            await AssertNoStoredSecretAsync(host, credential.Payload);
        }

        [SkippableFact]
        public async Task ResolvingAndThenConfirmingRecordsOneSuccessfulAttemptAndOneRead()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (record, patient) = await host.SignInPatientAsync("Alba");
            var credential = await IssueAsync(patient);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            var key = Guid.NewGuid();

            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(paramedic, emergency.Id, credential.Payload)).StatusCode);
            Assert.Empty(await SuccessfulAttemptsAsync(host, emergency.Id));

            Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(paramedic, emergency.Id, credential.Payload, key)).StatusCode);
            var attempt = Assert.Single(await SuccessfulAttemptsAsync(host, emergency.Id));
            Assert.Equal((IdentificationMethod.QrCode, (Guid?)record.PatientId), (attempt.Method, attempt.PatientId));

            Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(paramedic, emergency.Id, credential.Payload, key)).StatusCode);
            Assert.Single(await SuccessfulAttemptsAsync(host, emergency.Id));
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergency.Id && a.Action == AuditActionType.Identify)));
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(
                a => a.ClinicalEncounterId == emergency.Id && a.EntityName == EmergencyQrIdentificationService.CandidateAuditResource)));
            await AssertNoStoredSecretAsync(host, credential.Payload);
        }

        [SkippableFact]
        public async Task AnInvalidQrAtConfirmationIsTheSameNotFoundAndASafeFailure()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var clock = Clock();
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, mobileServices: s => s.AddSingleton<TimeProvider>(clock));
            var (_, patient) = await host.SignInPatientAsync("Alba");
            var replaced = await IssueAsync(patient);
            var expired = await IssueAsync(patient);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            clock.Advance(PatientQrToken.Lifetime);

            foreach (var (payload, reason) in new[]
            {
                ("not-a-qr", IdentificationFailureReason.NoMatch),
                (PatientQrToken.Generate().Payload, IdentificationFailureReason.NoMatch),
                (replaced.Payload, IdentificationFailureReason.InvalidCredential),
                (expired.Payload, IdentificationFailureReason.InvalidCredential)
            })
            {
                var key = Guid.NewGuid();
                var before = await host.Web.DbAsync(db => db.IdentificationAttempts.CountAsync(a => a.FailureReason == reason));

                Assert.Equal(HttpStatusCode.NotFound, (await ConfirmAsync(paramedic, emergency.Id, payload, key)).StatusCode);

                Assert.Equal(before + 1, await host.Web.DbAsync(db => db.IdentificationAttempts.CountAsync(a => a.FailureReason == reason)));
                Assert.Equal(0, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.ClientRequestId == key)));
            }

            var attempts = await host.Web.DbAsync(db => db.IdentificationAttempts.AsNoTracking().ToListAsync());
            Assert.Equal(4, attempts.Count);
            Assert.All(attempts, a => Assert.Equal(
                (IdentificationMethod.QrCode, IdentificationOutcome.Failure, (Guid?)null, (Guid?)null),
                (a.Method, a.Outcome, a.PatientId, a.PatientIdentifierId)));
            var encounter = await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == emergency.Id));
            Assert.Null(encounter.PatientId);
            await AssertNoStoredSecretAsync(host, replaced.Payload);
            await AssertNoStoredSecretAsync(host, expired.Payload);
        }

        [SkippableFact]
        public async Task ConcurrentConfirmationsLeaveOnePatient()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var (a, patientA) = await host.SignInPatientAsync("Alba");
            var (b, patientB) = await host.SignInPatientAsync("Bruno");
            var qrA = await IssueAsync(patientA);
            var qrB = await IssueAsync(patientB);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            var sameKey = Guid.NewGuid();

            var responses = await Task.WhenAll(
                ConfirmAsync(paramedic, emergency.Id, qrA.Payload, sameKey),
                ConfirmAsync(paramedic, emergency.Id, qrA.Payload, sameKey),
                ConfirmAsync(paramedic, emergency.Id, qrB.Payload));

            Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"Unexpected {r.StatusCode}"));
            var encounter = await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == emergency.Id));
            Assert.Contains(encounter.PatientId!.Value, new[] { a.PatientId, b.PatientId });
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(e => e.ClinicalEncounterId == emergency.Id && e.Action == AuditActionType.Identify)));
            var attempt = Assert.Single(await SuccessfulAttemptsAsync(host, emergency.Id));
            Assert.Equal(encounter.PatientId, attempt.PatientId);
        }

        // ---- Rate limits ----

        [SkippableFact]
        public async Task PatientsHaveTheirOwnQrLimit()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, new Dictionary<string, string?> { ["RateLimiting:PatientQrPermitLimit"] = "2" });
            var (a, clientA) = await host.SignInPatientAsync("Alba");
            var (_, clientB) = await host.SignInPatientAsync("Bruno");

            Assert.Equal(HttpStatusCode.OK, (await clientA.PostAsync(Qr, null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await clientA.DeleteAsync(Qr)).StatusCode);
            var limited = await clientA.PostAsync(Qr, null);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.True(limited.Headers.Contains("Retry-After"));
            Assert.Equal(HttpStatusCode.TooManyRequests, (await clientA.DeleteAsync(Qr)).StatusCode);

            // Another patient and the record reads of the same patient are unaffected; nothing was issued past the limit.
            Assert.Equal(HttpStatusCode.OK, (await clientB.PostAsync(Qr, null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync("/api/mobile/patient/me")).StatusCode);
            Assert.Single(await QrRowsAsync(host, a.PatientId));
        }

        [SkippableFact]
        public async Task StaffHaveTheirOwnResolveLimit()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, new Dictionary<string, string?> { ["RateLimiting:PatientQrResolvePermitLimit"] = "2" });
            var (_, patient) = await host.SignInPatientAsync("Alba");
            var credential = await IssueAsync(patient);
            var clinic = await host.Web.CreateClinicAsync();
            var first = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var second = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var firstEmergency = await OpenAsync(first);
            var secondEmergency = await OpenAsync(second);

            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(first, firstEmergency.Id, credential.Payload)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync(first, firstEmergency.Id, PatientQrToken.Generate().Payload)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await ResolveAsync(first, firstEmergency.Id, credential.Payload)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await ConfirmAsync(first, firstEmergency.Id, credential.Payload)).StatusCode);

            // Not shared among staff, and the rest of the emergency API goes on.
            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(second, secondEmergency.Id, credential.Payload)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await first.Client.GetAsync($"{Emergencies}/{firstEmergency.Id}")).StatusCode);
        }

        // ---- Audit fail closed ----

        internal sealed class SaveSwitch
        {
            public volatile bool Fail;
        }

        public class SwitchedSaveProxy : DispatchProxy
        {
            internal IClinicalWriteStore Inner { get; set; } = null!;
            internal SaveSwitch Switch { get; set; } = null!;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
                targetMethod!.Name == nameof(IClinicalWriteStore.SaveChangesAsync) && Switch.Fail
                    ? Task.FromException(new InvalidOperationException("audit-store-internal-detail"))
                    : targetMethod.Invoke(Inner, args);
        }

        private static void FailSavesWhen(IServiceCollection services, SaveSwitch saves)
        {
            var real = services.Single(d => d.ServiceType == typeof(IClinicalWriteStore));
            services.Remove(real);
            services.AddScoped(provider =>
            {
                var proxy = DispatchProxy.Create<IClinicalWriteStore, SwitchedSaveProxy>();
                var inner = (SwitchedSaveProxy)(object)proxy;
                inner.Inner = (IClinicalWriteStore)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!);
                inner.Switch = saves;
                return proxy;
            });
        }

        [SkippableFact]
        public async Task WithoutItsAuditNoCandidateLeavesAndNoCredentialChanges()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var saves = new SaveSwitch();
            await using var host = await MobileEmergencyHost.StartAsync(_postgres, mobileServices: s => FailSavesWhen(s, saves));
            var (record, patient) = await host.SignInPatientAsync("Alba");
            var credential = await IssueAsync(patient);
            var paramedic = await ParamedicAsync(host);
            var emergency = await OpenAsync(paramedic);
            saves.Fail = true;

            var resolve = await ResolveAsync(paramedic, emergency.Id, credential.Payload);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, resolve.StatusCode);
            var body = await resolve.Content.ReadAsStringAsync();
            foreach (var hidden in new[] { record.GivenNames, "1990", "audit", "internal-detail" })
            {
                Assert.DoesNotContain(hidden, body, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var response in new[] { await patient.PostAsync(Qr, null), await patient.DeleteAsync(Qr) })
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.DoesNotContain("payload", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            }

            // The current credential is untouched and works again as soon as the audit does.
            var row = Assert.Single(await QrRowsAsync(host, record.PatientId));
            Assert.Equal((PatientIdentifierStatus.Active, HashOf(credential.Payload)), (row.Status, row.NormalizedValue));
            saves.Fail = false;
            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(paramedic, emergency.Id, credential.Payload)).StatusCode);

            // A failed attempt that cannot be recorded is still the same 404.
            saves.Fail = true;
            Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync(paramedic, emergency.Id, PatientQrToken.Generate().Payload)).StatusCode);
        }
    }
}
