using System.Diagnostics.Metrics;
using System.Text.Json;
using SCI_PH.Application.Abstractions.AI;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.Tests.Clinical
{
    // The external-provider rules of the assisted encounter summary (ADR-014) over the fakes: Export audit when the input
    // left SCI-PH (Read when it did not), token usage in the audit and the meter, the single regeneration of an unreadable
    // or rejected answer, failure categories, and the in-memory cache (per user, per input fingerprint, 10 minutes,
    // validated answers only, a hit audited as a Read with the original GeneratedAtUtc). The in-process Fake is not cached.
    public sealed class EncounterAiExternalProviderTests
    {
        private static readonly ClinicalAiUsage _usage = new(1200, 400, 0);

        private sealed record Seeded(ClinicalHarness Harness, ClinicalEncounter Encounter);

        private static Seeded Seed(bool external = true)
        {
            var h = ClinicalHarness.ForRole(SecurityRoles.Doctor);
            var patient = h.SeedPatient();
            var encounter = h.SeedEncounter(patientId: patient.Id);
            h.Store.Triages.Add(Triage.Record(encounter.Id, TriagePriority.Urgent, h.StaffId, ClinicalHarness.Start.AddMinutes(5), "Dolor", null));
            h.Store.VitalSigns.Add(VitalSign.Record(encounter.Id, VitalSignType.HeartRate, 98, ClinicalHarness.Start.AddMinutes(10), h.StaffId));
            h.AiProvider.SendsDataExternally = external;
            h.AiProvider.Answer = (input, _) => Task.FromResult(TestClinicalAiProvider.ValidDraft(input) with { Usage = _usage });
            return new Seeded(h, encounter);
        }

        private static Task<EncounterAiSummary> GenerateAsync(Seeded s) => s.Harness.AiSummaries.GenerateAsync(s.Encounter.Id, CancellationToken.None);

        private static List<ClinicalAuditEntry> Audits(ClinicalHarness h) =>
            h.Store.Audit.Where(a => a.EntityName == EncounterAiSummaryService.EntityName).ToList();

        private static JsonElement Changes(ClinicalAuditEntry audit) => JsonDocument.Parse(audit.ChangesJson!).RootElement;

        [Fact]
        public async Task ASummaryFromAnExternalProviderIsAuditedAsAnExportWithItsUsage()
        {
            var s = Seed();
            await GenerateAsync(s);

            var audit = Assert.Single(Audits(s.Harness));
            Assert.Equal((AuditActionType.Export, AuditOutcome.Success), (audit.Action, audit.Outcome));
            var changes = Changes(audit);
            Assert.Equal(1, changes.GetProperty("providerCalls").GetInt32());
            Assert.False(changes.GetProperty("cacheHit").GetBoolean());
            Assert.Equal(1200, changes.GetProperty("usage").GetProperty("inputTokens").GetInt32());
            Assert.Equal(400, changes.GetProperty("usage").GetProperty("outputTokens").GetInt32());
            Assert.Equal(JsonValueKind.Null, changes.GetProperty("category").ValueKind);
        }

        [Fact]
        public async Task TheInProcessProviderIsAReadAndIsNeverCached()
        {
            var s = Seed(external: false);
            var first = await GenerateAsync(s);
            s.Harness.Time.Now = s.Harness.Time.Now.AddMinutes(1);
            var second = await GenerateAsync(s);

            Assert.Equal(2, s.Harness.AiProvider.Inputs.Count);
            Assert.NotEqual(first.GeneratedAtUtc, second.GeneratedAtUtc);
            Assert.All(Audits(s.Harness), a => Assert.Equal(AuditActionType.Read, a.Action));
            Assert.All(Audits(s.Harness), a => Assert.False(Changes(a).GetProperty("cacheHit").GetBoolean()));
        }

        [Fact]
        public async Task ARepeatedRequestIsAnsweredFromTheCacheAsARead()
        {
            var s = Seed();
            var first = await GenerateAsync(s);
            s.Harness.Time.Now = s.Harness.Time.Now.AddMinutes(9);
            var second = await GenerateAsync(s);

            Assert.Single(s.Harness.AiProvider.Inputs);
            Assert.Same(first, second);
            Assert.Equal(first.GeneratedAtUtc, second.GeneratedAtUtc);
            var audits = Audits(s.Harness);
            Assert.Equal([AuditActionType.Export, AuditActionType.Read], audits.Select(a => a.Action));
            var hit = Changes(audits[1]);
            Assert.True(hit.GetProperty("cacheHit").GetBoolean());
            Assert.Equal(0, hit.GetProperty("providerCalls").GetInt32());
            Assert.Equal(JsonValueKind.Null, hit.GetProperty("usage").ValueKind);
            Assert.Equal(first.SourceFingerprint, hit.GetProperty("sourceFingerprint").GetString());
            Assert.Contains(s.Harness.AiLogs.Lines, l => l.Contains(": CacheHit,"));
        }

        [Fact]
        public async Task AnEntryExpiresTenMinutesAfterItWasStored()
        {
            var s = Seed();
            await GenerateAsync(s);
            s.Harness.Time.Now = s.Harness.Time.Now.Add(EncounterAiSummaryService.CacheTimeToLive).AddSeconds(1);
            await GenerateAsync(s);

            Assert.Equal(2, s.Harness.AiProvider.Inputs.Count);
            Assert.All(Audits(s.Harness), a => Assert.Equal(AuditActionType.Export, a.Action));
        }

        [Fact]
        public async Task ANewRecordChangesTheFingerprintAndCallsTheProviderAgain()
        {
            var s = Seed();
            var first = await GenerateAsync(s);
            s.Harness.Store.VitalSigns.Add(VitalSign.Record(s.Encounter.Id, VitalSignType.HeartRate, 110, ClinicalHarness.Start.AddMinutes(20), s.Harness.StaffId));
            var second = await GenerateAsync(s);

            Assert.Equal(2, s.Harness.AiProvider.Inputs.Count);
            Assert.NotEqual(first.SourceFingerprint, second.SourceFingerprint);
            Assert.Equal(2, s.Harness.AiProvider.Inputs[1].VitalSigns.Count);
        }

        [Fact]
        public async Task TheCacheIsPartitionedPerUser()
        {
            var s = Seed();
            await GenerateAsync(s);

            // A second clinician of the same clinic, same host singletons (provider, cache), same encounter and input.
            var nurse = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Nurse), s.Harness.Store, sharedAi: s.Harness);
            nurse.Staff.Add(nurse.User.UserId!.Value, true, s.Encounter.ClinicId!.Value);
            var other = await nurse.AiSummaries.GenerateAsync(s.Encounter.Id, CancellationToken.None);

            Assert.Equal(2, s.Harness.AiProvider.Inputs.Count);
            Assert.Equal(AuditActionType.Export, Audits(s.Harness)[^1].Action);
            Assert.False(Changes(Audits(s.Harness)[^1]).GetProperty("cacheHit").GetBoolean());

            // Each user then hits only their own entry.
            s.Harness.Time.Now = s.Harness.Time.Now.AddMinutes(1);
            Assert.Equal(other.GeneratedAtUtc, (await nurse.AiSummaries.GenerateAsync(s.Encounter.Id, CancellationToken.None)).GeneratedAtUtc);
            await GenerateAsync(s);
            Assert.Equal(2, s.Harness.AiProvider.Inputs.Count);
        }

        [Fact]
        public async Task OnlyAValidatedSummaryIsCached()
        {
            var s = Seed();
            s.Harness.AiProvider.Answer = (input, _) => Task.FromResult(TestClinicalAiProvider.ValidDraft(input) with { Summary = null });
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => GenerateAsync(s));
            s.Harness.AiProvider.Answer = (_, _) => throw new ClinicalAiProviderException(ClinicalAiFailure.ContentFiltered, "filtered");
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => GenerateAsync(s));
            s.Harness.AiProvider.Answer = (input, _) => Task.FromResult(TestClinicalAiProvider.ValidDraft(input));
            await GenerateAsync(s);

            // Two calls for the rejected draft (one regeneration), one for the filter, one success: no failure was reused.
            Assert.Equal(4, s.Harness.AiProvider.Inputs.Count);
            Assert.Equal([false, false, false], Audits(s.Harness).Select(a => Changes(a).GetProperty("cacheHit").GetBoolean()));
        }

        [Fact]
        public async Task AnUnreadableAnswerIsRegeneratedOnceAndTheSecondAnswerIsUsed()
        {
            var s = Seed();
            var calls = 0;
            s.Harness.AiProvider.Answer = (input, _) => ++calls == 1
                ? throw new ClinicalAiOutputException("not json", usage: new ClinicalAiUsage(1000, 50))
                : Task.FromResult(TestClinicalAiProvider.ValidDraft(input) with { Usage = _usage });

            var summary = await GenerateAsync(s);

            Assert.True(summary.AiGenerated);
            var changes = Changes(Assert.Single(Audits(s.Harness)));
            Assert.Equal(2, changes.GetProperty("providerCalls").GetInt32());
            Assert.Equal(2200, changes.GetProperty("usage").GetProperty("inputTokens").GetInt32());
            Assert.Equal(450, changes.GetProperty("usage").GetProperty("outputTokens").GetInt32());
        }

        [Fact]
        public async Task ARejectedDraftIsRegeneratedAtMostOnce()
        {
            var s = Seed();
            s.Harness.AiProvider.Answer = (input, _) => Task.FromResult(TestClinicalAiProvider.ValidDraft(input,
                [new ReviewPointDraft("PendingItem", "Pendiente.", [new SourceDraft("VitalSign", "V999")])]) with { Usage = _usage });

            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => GenerateAsync(s));

            Assert.Equal(EncounterAiSummaryService.MaxProviderCalls, s.Harness.AiProvider.Inputs.Count);
            var audit = Assert.Single(Audits(s.Harness));
            Assert.Equal((AuditActionType.Export, AuditOutcome.Failure, "UnknownSource"), (audit.Action, audit.Outcome, audit.FailureReason));
            var changes = Changes(audit);
            Assert.Equal("InvalidStructuredOutput", changes.GetProperty("category").GetString());
            Assert.Equal(2, changes.GetProperty("providerCalls").GetInt32());
            Assert.Equal(2400, changes.GetProperty("usage").GetProperty("inputTokens").GetInt32());
        }

        [Fact]
        public async Task NoRegenerationOnceHalfTheTimeoutHasPassed()
        {
            var s = Seed();
            s.Harness.AiProvider.Timeout = TimeSpan.FromMilliseconds(400);
            s.Harness.AiProvider.Answer = async (_, _) =>
            {
                await Task.Delay(250);
                throw new ClinicalAiOutputException("not json");
            };

            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => GenerateAsync(s));
            Assert.Single(s.Harness.AiProvider.Inputs);
        }

        public static TheoryData<ClinicalAiFailure> NotRetried => new()
        {
            ClinicalAiFailure.RateLimitedByProvider, ClinicalAiFailure.QuotaExceeded, ClinicalAiFailure.AuthenticationFailed,
            ClinicalAiFailure.ContentFiltered, ClinicalAiFailure.ProviderUnavailable, ClinicalAiFailure.ConfigurationError, ClinicalAiFailure.Timeout
        };

        [Theory]
        [MemberData(nameof(NotRetried))]
        public async Task AProviderFailureIsNeverRegeneratedAndKeepsItsCategory(ClinicalAiFailure failure)
        {
            var s = Seed();
            s.Harness.AiProvider.Answer = (_, _) => throw new ClinicalAiProviderException(failure, "upstream detail 4711", usage: new ClinicalAiUsage(900, 0));

            var error = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => GenerateAsync(s));

            Assert.Equal("The assisted summary is not available right now. Try again later.", error.Message);
            Assert.Single(s.Harness.AiProvider.Inputs);
            var audit = Assert.Single(Audits(s.Harness));
            Assert.Equal((AuditActionType.Export, AuditOutcome.Failure, "ProviderUnavailable"), (audit.Action, audit.Outcome, audit.FailureReason));
            Assert.Equal(failure.ToString(), Changes(audit).GetProperty("category").GetString());
            Assert.Equal(900, Changes(audit).GetProperty("usage").GetProperty("inputTokens").GetInt32());
            Assert.DoesNotContain("4711", audit.ChangesJson);
            Assert.DoesNotContain(s.Harness.AiLogs.Lines, l => l.Contains("4711"));
        }

        [Fact]
        public async Task ARejectionBeforeTransmissionIsARead()
        {
            var s = Seed();
            s.Harness.AiProvider.Answer = (_, _) =>
                throw new ClinicalAiProviderException(ClinicalAiFailure.ConfigurationError, "too large", beforeTransmission: true);

            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => GenerateAsync(s));

            var audit = Assert.Single(Audits(s.Harness));
            Assert.Equal((AuditActionType.Read, AuditOutcome.Failure), (audit.Action, audit.Outcome));
            Assert.Equal("ConfigurationError", Changes(audit).GetProperty("category").GetString());
        }

        [Fact]
        public async Task ADisabledExternalProviderIsAReadWithoutCall()
        {
            var s = Seed();
            s.Harness.AiProvider.IsEnabled = false;
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => GenerateAsync(s));
            Assert.Equal(AuditActionType.Read, Assert.Single(Audits(s.Harness)).Action);
            Assert.Empty(s.Harness.AiProvider.Inputs);
        }

        [Fact]
        public async Task ATimeoutOfAnExternalProviderIsAnExportCategorizedAsTimeout()
        {
            var s = Seed();
            s.Harness.AiProvider.Timeout = TimeSpan.FromMilliseconds(100);
            s.Harness.AiProvider.Answer = async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException();
            };

            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => GenerateAsync(s));

            Assert.Single(s.Harness.AiProvider.Inputs);
            var audit = Assert.Single(Audits(s.Harness));
            Assert.Equal((AuditActionType.Export, "Timeout"), (audit.Action, audit.FailureReason));
            Assert.Equal("Timeout", Changes(audit).GetProperty("category").GetString());
        }

        [Fact]
        public async Task TheMeterCountsRequestsFailuresTokensAndCacheHitsWithLowCardinalityTags()
        {
            var s = Seed();
            var measurements = new List<(string Instrument, long Value, Dictionary<string, object?> Tags)>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (ReferenceEquals(instrument.Meter, s.Harness.AiMetrics.Meter))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                measurements.Add((instrument.Name, 1, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
            listener.Start();

            await GenerateAsync(s);
            await GenerateAsync(s);
            s.Harness.Store.VitalSigns.Add(VitalSign.Record(s.Encounter.Id, VitalSignType.HeartRate, 110, ClinicalHarness.Start.AddMinutes(20), s.Harness.StaffId));
            s.Harness.AiProvider.Answer = (_, _) => throw new ClinicalAiProviderException(ClinicalAiFailure.RateLimitedByProvider, "429");
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => GenerateAsync(s));

            Assert.Equal("SCI-PH.ClinicalAi", ClinicalAiMetrics.MeterName);
            Assert.Equal(["Success", "CacheHit", "RateLimitedByProvider"],
                measurements.Where(m => m.Instrument == "ai_requests_total").Select(m => m.Tags["outcome"]));
            Assert.Equal("RateLimitedByProvider", Assert.Single(measurements, m => m.Instrument == "ai_failures_total").Tags["outcome"]);
            Assert.Equal(1200, Assert.Single(measurements, m => m.Instrument == "ai_input_tokens").Value);
            Assert.Equal(400, Assert.Single(measurements, m => m.Instrument == "ai_output_tokens").Value);
            Assert.Single(measurements, m => m.Instrument == "ai_cache_hits_total");
            Assert.Single(measurements, m => m.Instrument == "ai_request_duration");

            // Only provider, model, operation and outcome: never an id, a fingerprint or content.
            var allowed = new[] { "provider", "model", "operation", "outcome" };
            Assert.All(measurements, m => Assert.All(m.Tags.Keys, k => Assert.Contains(k, allowed)));
            var values = string.Join("|", measurements.SelectMany(m => m.Tags.Values.Select(v => v?.ToString())));
            Assert.DoesNotContain(s.Encounter.Id.ToString(), values);
            Assert.DoesNotContain(s.Harness.User.UserId!.Value.ToString(), values);
            Assert.Equal(["SCIPH-AiTest"], measurements.Select(m => m.Tags["provider"]).Distinct());
        }
    }
}
