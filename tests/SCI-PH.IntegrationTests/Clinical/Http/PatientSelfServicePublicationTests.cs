using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.Security.Http;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Mobile.BFF.Controllers;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    internal static class PatientSelfHttp
    {
        public const string Me = "/api/mobile/patient/me";

        public static readonly string[] Routes =
            [Me, $"{Me}/summary", $"{Me}/encounters", $"{Me}/allergies", $"{Me}/diagnoses", $"{Me}/prescriptions", $"{Me}/results"];

        public static async Task<(SeededPatientRecord Record, HttpClient Client, Guid UserId)> SignInPatientAsync(
            BffFactory<AuthController> factory, string givenNames)
        {
            var account = await factory.CreateUserAsync(SecurityRoles.Patient);
            await using var scope = factory.Services.CreateAsyncScope();
            var record = await PatientRecordSeed.SeedAsync(
                scope.ServiceProvider.GetRequiredService<ClinicalDbContext>(), $"{givenNames}{Guid.NewGuid():N}"[..(givenNames.Length + 6)], account.Id);
            return (record, await LoginAsync(factory, account), account.Id);
        }

        public static async Task<HttpClient> LoginAsync(BffFactory<AuthController> factory, TestAccount account)
        {
            var login = await factory.CreateHttpsClient().PostAsJsonAsync("/api/mobile/auth/login", new LoginRequest(account.Email, account.Password));
            login.EnsureSuccessStatusCode();
            var session = (await login.ReadAsAsync<MobileLoginResponse>()).Session!;
            return factory.CreateHttpsClient().WithBearer(session.AccessToken);
        }

        public static async Task<List<ClinicalAuditEntry>> AuditAsync(BffFactory<AuthController> factory, Func<ClinicalAuditEntry, bool> filter)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var entries = await scope.ServiceProvider.GetRequiredService<ClinicalDbContext>().ClinicalAuditEntries.AsNoTracking().ToListAsync();
            return entries.Where(filter).ToList();
        }
    }

    // Conservative V1 publication rules through the real Mobile BFF: what each route lets out, and what never leaves the API.
    public sealed class PatientSelfServicePublicationTests : IClassFixture<MobileBffFactory>
    {
        private const string Me = PatientSelfHttp.Me;
        private readonly MobileBffFactory _factory;

        public PatientSelfServicePublicationTests(MobileBffFactory factory) => _factory = factory;

        private static IEnumerable<string> AllRoutes(SeededPatientRecord record) =>
            PatientSelfHttp.Routes.Concat([$"{Me}/encounters/{record.EncounterId}", $"{Me}/encounters/{record.CancelledEncounterId}"]);

        [Fact]
        public async Task OnlyDefinitiveDiagnosesOfAClosedEncounterArePublished()
        {
            var (record, client, _) = await PatientSelfHttp.SignInPatientAsync(_factory, "Ana");

            var diagnoses = (await client.GetFromJsonAsync<List<PatientSelfDiagnosis>>($"{Me}/diagnoses"))!;

            var published = Assert.Single(diagnoses);
            Assert.Equal((record.DiagnosisId, DiagnosisType.Definitive), (published.Id, published.Type));
            foreach (var route in AllRoutes(record))
            {
                var body = await client.GetStringAsync(route);
                foreach (var hidden in new[] { record.PresumptiveDiagnosisId, record.InProgressDiagnosisId, record.CancelledDiagnosisId })
                {
                    Assert.DoesNotContain(hidden.ToString(), body);
                }

                Assert.DoesNotContain("Presuntivo", body);
                Assert.DoesNotContain("Presumptive", body);
            }
        }

        [Fact]
        public async Task AnEncounterStillInProgressPublishesNoDiagnosis()
        {
            var (record, client, _) = await PatientSelfHttp.SignInPatientAsync(_factory, "Ana");

            var detail = await client.GetFromJsonAsync<PatientSelfEncounterDetail>($"{Me}/encounters/{record.OlderEncounterId}");

            Assert.Equal(PatientEncounterStatus.InProgress, detail!.Encounter.Status);
            Assert.Empty(detail.Diagnoses);
        }

        [Fact]
        public async Task ACancelledEncounterAppearsOnlyAsItself()
        {
            var (record, client, _) = await PatientSelfHttp.SignInPatientAsync(_factory, "Ana");

            var encounters = (await client.GetFromJsonAsync<List<PatientSelfEncounter>>($"{Me}/encounters"))!;
            var cancelled = Assert.Single(encounters, e => e.Id == record.CancelledEncounterId);
            Assert.Equal(PatientEncounterStatus.Cancelled, cancelled.Status);

            var detail = await client.GetFromJsonAsync<PatientSelfEncounterDetail>($"{Me}/encounters/{record.CancelledEncounterId}");
            Assert.Equal(PatientEncounterStatus.Cancelled, detail!.Encounter.Status);
            Assert.Empty(detail.Diagnoses);
            Assert.Empty(detail.Results);
            Assert.Empty(detail.Prescriptions);

            foreach (var route in PatientSelfHttp.Routes)
            {
                var body = await client.GetStringAsync(route);
                foreach (var hidden in new[] { record.CancelledDiagnosisId, record.CancelledResultId, record.CancelledPrescriptionId })
                {
                    Assert.DoesNotContain(hidden.ToString(), body);
                }
            }
        }

        [Fact]
        public async Task InternalWorkflowStatesNeverLeaveTheApi()
        {
            var (record, client, _) = await PatientSelfHttp.SignInPatientAsync(_factory, "Ana");

            var encounters = (await client.GetFromJsonAsync<List<PatientSelfEncounter>>($"{Me}/encounters"))!;

            Assert.Equal(
                [(record.EncounterId, PatientEncounterStatus.Completed), (record.CancelledEncounterId, PatientEncounterStatus.Cancelled), (record.OlderEncounterId, PatientEncounterStatus.InProgress)],
                encounters.Select(e => (e.Id, e.Status)));
            using var raw = JsonDocument.Parse(await client.GetStringAsync($"{Me}/encounters"));
            Assert.Equal(["Completed", "Cancelled", "InProgress"], raw.RootElement.EnumerateArray().Select(e => e.GetProperty("status").GetString()));
        }

        [Fact]
        public async Task TheHomeSummaryCountsOnlyWhatTheListsPublish()
        {
            var (record, client, _) = await PatientSelfHttp.SignInPatientAsync(_factory, "Ana");

            var summary = (await client.GetFromJsonAsync<PatientSelfSummary>($"{Me}/summary"))!;
            var encounters = (await client.GetFromJsonAsync<List<PatientSelfEncounter>>($"{Me}/encounters"))!;
            var results = (await client.GetFromJsonAsync<List<PatientSelfResult>>($"{Me}/results"))!;

            Assert.Equal(encounters.Count, summary.EncounterCount);
            Assert.Equal(results.Count, summary.ResultCount);
            Assert.Equal(2, summary.ResultCount);
            Assert.DoesNotContain(results, r => r.Id == record.CancelledResultId || r.Id == record.PendingResultId);
            Assert.Equal(results[0].Id, summary.LatestResult!.Id);
            Assert.Equal(encounters[0].Id, summary.LatestEncounter!.Id);
        }

        [Fact]
        public async Task OnlyFinalAndCorrectedResultsArePublishedWithoutInterpretation()
        {
            var (record, client, _) = await PatientSelfHttp.SignInPatientAsync(_factory, "Ana");

            var results = (await client.GetFromJsonAsync<List<PatientSelfResult>>($"{Me}/results"))!;

            Assert.Equal(
                [(record.FinalResultId, ClinicalResultStatus.Final), (record.CorrectedResultId, ClinicalResultStatus.Corrected)],
                results.Select(r => (r.Id, r.Status)));
            Assert.DoesNotContain("interpretation", await client.GetStringAsync($"{Me}/results"), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task InternalFieldsAreAbsentFromEveryResponse()
        {
            var (record, client, _) = await PatientSelfHttp.SignInPatientAsync(_factory, "Ana");
            string[] forbidden =
            [
                "notes", "identityStatus", "reason", "codeSystem", "code", "isPrimary", "confirmedAtUtc", "temporaryPatientCode",
                "ambulanceId", "recordedByStaffId", "prescribedByStaffId", "biometricReferences", "subjectReference", "provider",
                "algorithmVersion", "deviceCredentialId", "slot", "blobPath", "vitalSigns", "procedures", "triage", "audit"
            ];

            foreach (var route in AllRoutes(record))
            {
                var body = await client.GetStringAsync(route);
                using var json = JsonDocument.Parse(body);
                var names = PropertyNames(json.RootElement).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Assert.Empty(names.Intersect(forbidden, StringComparer.OrdinalIgnoreCase));

                // Their values cannot appear either: encounter reason, prescriber note, clinical note, vital sign, procedure.
                foreach (var marker in new[] { PatientRecordSeed.ReasonMarker, PatientRecordSeed.PrescriptionNoteMarker, PatientRecordSeed.ClinicalNoteMarker, PatientRecordSeed.ProcedureMarker })
                {
                    Assert.DoesNotContain(marker, body);
                }
            }
        }

        [Theory]
        [InlineData("notes")]
        [InlineData("clinical-notes")]
        [InlineData("vital-signs")]
        [InlineData("procedures")]
        [InlineData("documents")]
        [InlineData("audit")]
        [InlineData("biometrics")]
        [InlineData("face")]
        public async Task WithheldCategoriesHaveNoRoute(string category)
        {
            var (_, client, _) = await PatientSelfHttp.SignInPatientAsync(_factory, "Ana");

            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Me}/{category}")).StatusCode);
        }

        [Fact]
        public async Task EachReadIsAuditedOnceWithMetadataOnly()
        {
            var (record, client, userId) = await PatientSelfHttp.SignInPatientAsync(_factory, "Ana");

            foreach (var route in PatientSelfHttp.Routes.Append($"{Me}/encounters/{record.EncounterId}"))
            {
                (await client.GetAsync(route)).EnsureSuccessStatusCode();
            }

            var entries = await PatientSelfHttp.AuditAsync(_factory, a => a.PatientId == record.PatientId);
            Assert.Equal(
                new[]
                {
                    PatientSelfAuditResources.Profile, PatientSelfAuditResources.Summary, PatientSelfAuditResources.Encounters,
                    PatientSelfAuditResources.Allergies, PatientSelfAuditResources.Diagnoses, PatientSelfAuditResources.Prescriptions,
                    PatientSelfAuditResources.Results, PatientSelfAuditResources.Encounter
                }.Order(),
                entries.Select(e => e.EntityName).Order());
            Assert.All(entries, entry =>
            {
                Assert.Equal((AuditActionType.Read, AuditOutcome.Success, AuditActorType.User), (entry.Action, entry.Outcome, entry.ActorType));
                Assert.Equal(userId, entry.ActorIdentityUserId);
                Assert.Null(entry.ChangesJson);
                Assert.Null(entry.FailureReason);
                Assert.DoesNotContain(record.GivenNames, entry.EntityId);
            });
        }

        [Fact]
        public async Task AnotherPatientsEncounterIsAuditedAsAFailureOfTheCaller()
        {
            var (a, clientA, userA) = await PatientSelfHttp.SignInPatientAsync(_factory, "Ana");
            var (b, _, _) = await PatientSelfHttp.SignInPatientAsync(_factory, "Berta");

            Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"{Me}/encounters/{b.EncounterId}")).StatusCode);

            var entry = Assert.Single(await PatientSelfHttp.AuditAsync(_factory, e => e.ActorIdentityUserId == userA));
            Assert.Equal(
                (AuditOutcome.Failure, "NotFound", (Guid?)a.PatientId, b.EncounterId.ToString(), (Guid?)null),
                (entry.Outcome, entry.FailureReason, entry.PatientId, entry.EntityId, entry.ClinicalEncounterId));
            Assert.Empty(await PatientSelfHttp.AuditAsync(_factory, e => e.PatientId == b.PatientId));
        }

        [Fact]
        public async Task AnUnlinkedPatientAccountIsRejectedAndAudited()
        {
            var account = await _factory.CreateUserAsync(SecurityRoles.Patient);
            var client = await PatientSelfHttp.LoginAsync(_factory, account);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Me)).StatusCode);

            var entry = Assert.Single(await PatientSelfHttp.AuditAsync(_factory, e => e.ActorIdentityUserId == account.Id));
            Assert.Equal((AuditOutcome.Failure, "PatientNotLinked", (Guid?)null), (entry.Outcome, entry.FailureReason, entry.PatientId));
        }

        private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
            JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
            _ => []
        };
    }

    // Fail closed: an otherwise authorized read whose audit entry cannot be saved returns a generic 503 and no payload.
    public sealed class PatientSelfServiceAuditFailureTests : IClassFixture<PatientSelfServiceAuditFailureTests.AuditDownFactory>
    {
        public const string InternalFailure = "audit-table-unavailable-internal-detail";

        private readonly AuditDownFactory _factory;

        public PatientSelfServiceAuditFailureTests(AuditDownFactory factory) => _factory = factory;

        public sealed class AuditDownFactory : BffFactory<AuthController>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.ConfigureTestServices(services =>
                {
                    var real = services.Single(d => d.ServiceType == typeof(IClinicalWriteStore));
                    services.Remove(real);
                    services.AddScoped(provider =>
                    {
                        var proxy = DispatchProxy.Create<IClinicalWriteStore, SaveFailsProxy>();
                        ((SaveFailsProxy)(object)proxy).Inner = (IClinicalWriteStore)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!);
                        return proxy;
                    });
                });
            }
        }

        public class SaveFailsProxy : DispatchProxy
        {
            public IClinicalWriteStore Inner { get; set; } = null!;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
                targetMethod!.Name == nameof(IClinicalWriteStore.SaveChangesAsync)
                    ? Task.FromException(new InvalidOperationException(InternalFailure))
                    : targetMethod.Invoke(Inner, args);
        }

        [Fact]
        public async Task NoPayloadLeavesWithoutItsAuditEntry()
        {
            var (record, client, _) = await PatientSelfHttp.SignInPatientAsync(_factory, "Ana");

            foreach (var route in PatientSelfHttp.Routes.Append($"{PatientSelfHttp.Me}/encounters/{record.EncounterId}"))
            {
                var response = await client.GetAsync(route);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(record.GivenNames, body);
                Assert.DoesNotContain(record.EncounterId.ToString(), body);
                Assert.DoesNotContain(InternalFailure, body);
                Assert.DoesNotContain("audit", body, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Empty(await PatientSelfHttp.AuditAsync(_factory, a => a.PatientId == record.PatientId));
        }

        [Fact]
        public async Task ADenialStaysADenialWhenItsAuditFails()
        {
            var (_, clientA, _) = await PatientSelfHttp.SignInPatientAsync(_factory, "Ana");
            var (b, _, _) = await PatientSelfHttp.SignInPatientAsync(_factory, "Berta");
            var unlinked = await PatientSelfHttp.LoginAsync(_factory, await _factory.CreateUserAsync(SecurityRoles.Patient));

            Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"{PatientSelfHttp.Me}/encounters/{b.EncounterId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await unlinked.GetAsync(PatientSelfHttp.Me)).StatusCode);
        }
    }

    // Per authenticated user: one patient exhausting the limit never affects another, nor the login limits.
    public sealed class PatientSelfServiceRateLimitTests
    {
        private const string Me = PatientSelfHttp.Me;
        private const int Limit = 3;

        private sealed class LowLimitFactory(string key) : BffFactory<AuthController>
        {
            protected override IDictionary<string, string?> Settings =>
                TestSettings.Create(new Dictionary<string, string?> { [key] = Limit.ToString() });
        }

        private static LowLimitFactory LowSelfServiceLimit() => new("RateLimiting:PatientSelfServicePermitLimit");

        private static LowLimitFactory LowAuthenticationLimit() => new("RateLimiting:AuthenticationPermitLimit");

        [Fact]
        public async Task APatientOverItsLimitGets429AndAnotherPatientIsUnaffected()
        {
            await using var factory = LowSelfServiceLimit();
            var (a, clientA, _) = await PatientSelfHttp.SignInPatientAsync(factory, "Ana");
            var (b, clientB, _) = await PatientSelfHttp.SignInPatientAsync(factory, "Berta");

            for (var i = 0; i < Limit; i++)
            {
                Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync(Me)).StatusCode);
            }

            var limited = await clientA.GetAsync(Me);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
            Assert.True(limited.Headers.Contains("Retry-After"));
            Assert.DoesNotContain(a.GivenNames, await limited.Content.ReadAsStringAsync());

            for (var i = 0; i < Limit; i++)
            {
                Assert.Equal(HttpStatusCode.OK, (await clientB.GetAsync($"{Me}/results")).StatusCode);
            }

            // A rejected request reaches neither the record nor the audit.
            Assert.Equal(Limit, (await PatientSelfHttp.AuditAsync(factory, e => e.PatientId == a.PatientId)).Count);
            Assert.Equal(Limit, (await PatientSelfHttp.AuditAsync(factory, e => e.PatientId == b.PatientId)).Count);
        }

        [Fact]
        public async Task TheSameAccountSharesItsLimitAcrossRoutesAndSessions()
        {
            await using var factory = LowSelfServiceLimit();
            var account = await factory.CreateUserAsync(SecurityRoles.Patient);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                await PatientRecordSeed.SeedAsync(scope.ServiceProvider.GetRequiredService<ClinicalDbContext>(), "Compartida", account.Id);
            }

            var first = await PatientSelfHttp.LoginAsync(factory, account);
            var second = await PatientSelfHttp.LoginAsync(factory, account);

            Assert.Equal(HttpStatusCode.OK, (await first.GetAsync(Me)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await second.GetAsync($"{Me}/results")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await first.GetAsync($"{Me}/summary")).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await second.GetAsync(Me)).StatusCode);
        }

        [Fact]
        public async Task AnonymousCallsFallBackToTheirIpAndDoNotSpendAPatientsLimit()
        {
            await using var factory = LowSelfServiceLimit();
            var (_, client, _) = await PatientSelfHttp.SignInPatientAsync(factory, "Ana");
            var anonymous = factory.CreateHttpsClient();

            for (var i = 0; i < Limit; i++)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Me)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, (await anonymous.GetAsync(Me)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);
        }

        [Fact]
        public async Task ExhaustingTheSelfServiceLimitDoesNotBlockSignIn()
        {
            await using var factory = LowSelfServiceLimit();
            var account = await factory.CreateUserAsync(SecurityRoles.Patient);
            var client = await PatientSelfHttp.LoginAsync(factory, account);

            for (var i = 0; i <= Limit; i++)
            {
                await client.GetAsync(Me);
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync(Me)).StatusCode);
            await PatientSelfHttp.LoginAsync(factory, account);
        }

        [Theory]
        [InlineData("login")]
        [InlineData("mfa/verify")]
        [InlineData("refresh")]
        public async Task TheAuthenticationLimitStillAppliesToAnonymousEndpoints(string route)
        {
            await using var factory = LowAuthenticationLimit();
            var client = factory.CreateHttpsClient();
            object body = route switch
            {
                "login" => new LoginRequest("nadie@sci-ph.test", "Aa1!incorrecta"),
                "refresh" => new { refreshToken = "not-a-token" },
                _ => new { challengeToken = "not-a-token", code = "123456" }
            };

            for (var i = 0; i < Limit; i++)
            {
                Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync($"/api/mobile/auth/{route}", body)).StatusCode);
            }

            var limited = await client.PostAsJsonAsync($"/api/mobile/auth/{route}", body);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task TheAuthenticationLimitDoesNotSpendThePatientReadLimit()
        {
            await using var factory = LowAuthenticationLimit();
            var (_, client, _) = await PatientSelfHttp.SignInPatientAsync(factory, "Ana");

            for (var i = 0; i < Limit * 3; i++)
            {
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);
            }
        }
    }
}
