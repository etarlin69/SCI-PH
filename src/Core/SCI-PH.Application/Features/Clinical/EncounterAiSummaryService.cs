using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SCI_PH.Application.Abstractions.AI;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Enums.Auditing;

namespace SCI_PH.Application.Features.Clinical
{
    // Assisted, read-only summary of one encounter with documentation review points (ADR-013, ADR-014). Same access as
    // reading the encounter (Encounters.Read, an active clinical actor of the encounter's clinic, the patient rule). The
    // provider gets only the minimized input of EncounterAiInputBuilder and its draft is accepted only whole by
    // EncounterAiOutputValidator; a draft that cannot be read or is rejected is regenerated at most ONCE, while less than
    // half of the timeout has passed (never a loop). The result is ephemeral: it is returned and never stored; nothing
    // clinical is written. The only write is one audit entry per request (Success or Failure) with safe metadata:
    // operation, provider, model, prompt version, input fingerprint, record counts, truncation, latency category, failure
    // and its category, provider calls, cache hit and token usage; never the input, prompt, answer or any clinical value.
    // Its action is Export when the input was (or may have been) transmitted to an external provider, Read otherwise
    // (in-process provider, Disabled, cache hit, rejected before transmission). With an external provider an identical
    // request of the same user within 10 minutes is answered from the summary cache (same GeneratedAtUtc, no new call).
    // Every failure of the provider (Disabled, unavailable, timeout, filtered, invalid or unknown-source answer) is
    // the same generic 503. A cancelled request (client gone) records nothing and stops the provider call.
    public sealed class EncounterAiSummaryService
    {
        public const string EntityName = "ClinicalAiEncounterSummary";
        public const string Operation = "encounter-summary";
        public const int MaxProviderCalls = 2;

        // The summary cache (ADR-014): deduplication of external calls (cost, data transfer), NOT idempotency. A dedicated
        // IMemoryCache registered by the composition root under this key (bounded, memory only, never the host's shared
        // cache); entries are keyed per user, encounter, input fingerprint, prompt, provider and model, hold only validated
        // summaries, are never serialized or logged and expire 10 minutes after they were stored.
        public const string CacheServiceKey = "clinical-ai-summaries";
        public static readonly TimeSpan CacheTimeToLive = TimeSpan.FromMinutes(10);

        private const string Unavailable = "The assisted summary is not available right now. Try again later.";

        private static readonly JsonSerializerOptions _auditJson = new(JsonSerializerDefaults.Web);

        private readonly ICurrentClinicalActor _actor;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly IClinicalAiService _ai;
        private readonly IMemoryCache _cache;
        private readonly ClinicalAiMetrics _metrics;
        private readonly TimeProvider _time;
        private readonly ILogger<EncounterAiSummaryService> _logger;

        public EncounterAiSummaryService(
            ICurrentClinicalActor actor,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IPatientAccessAuthorizer patientAccess,
            IClinicalAiService ai,
            [FromKeyedServices(CacheServiceKey)] IMemoryCache cache,
            ClinicalAiMetrics metrics,
            TimeProvider time,
            ILogger<EncounterAiSummaryService> logger)
        {
            _actor = actor;
            _reads = reads;
            _writes = writes;
            _patientAccess = patientAccess;
            _ai = ai;
            _cache = cache;
            _metrics = metrics;
            _time = time;
            _logger = logger;
        }

        public async Task<EncounterAiSummary> GenerateAsync(Guid encounterId, CancellationToken cancellationToken)
        {
            var encounter = await EncounterService.GetReadableAsync(_actor, _reads, _patientAccess, encounterId, cancellationToken);
            var actor = await _actor.GetAsync(cancellationToken);
            var context = new Attempt(actor, encounter.Id, encounter.PatientId);
            if (!_ai.IsEnabled)
            {
                throw await FailAsync(context, new Outcome("ProviderDisabled", null, null, null), prepared: null, cancellationToken);
            }

            var prepared = EncounterAiInputBuilder.Build(
                encounter,
                await _reads.ListTriagesAsync(encounter.Id, cancellationToken),
                await _reads.ListVitalSignsAsync(encounter.Id, cancellationToken),
                await _reads.ListNotesAsync(encounter.Id, cancellationToken),
                await _reads.ListDiagnosesAsync(encounter.Id, cancellationToken),
                await _reads.ListPrescriptionsAsync(encounter.Id, cancellationToken),
                await _reads.ListMedicationAdministrationsAsync(encounter.Id, cancellationToken),
                await _reads.ListProceduresAsync(encounter.Id, cancellationToken),
                await _reads.ListEncounterTransfersAsync(encounter.Id, EncounterAiInputBuilder.MaxTransfers + 1, cancellationToken));

            // Only an external provider is deduplicated (cost, data transfer); the in-process Fake answers every request.
            var key = new CacheKey(
                actor.IdentityUserId, encounter.Id, prepared.Fingerprint, _ai.PromptVersion, _ai.Provider, _ai.ModelVersion);
            if (_ai.SendsDataExternally && _cache.TryGetValue(key, out EncounterAiSummary? cached) && cached is not null)
            {
                return await CacheHitAsync(context, prepared, cached, cancellationToken);
            }

            var started = _time.GetTimestamp();
            var calls = 0;
            var transmitted = false;
            ClinicalAiUsage? usage = null;
            (string Summary, EncounterAiSections Sections, IReadOnlyList<EncounterAiReviewPoint> ReviewPoints, IReadOnlyList<string> NotDocumented) accepted;
            using (var timeout = new CancellationTokenSource(_ai.Timeout, _time))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (true)
                {
                    Outcome failed;
                    calls++;
                    try
                    {
                        var draft = await _ai.SummarizeEncounterAsync(prepared.Input, linked.Token);
                        transmitted = true;
                        usage = Add(usage, draft.Usage);
                        accepted = EncounterAiOutputValidator.Validate(draft, prepared, _ai.ModelVersion, _ai.PromptVersion);
                        break;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                    {
                        transmitted = true;
                        failed = new Outcome("Timeout", ClinicalAiFailure.Timeout, null, null);
                    }
                    catch (EncounterAiOutputRejectedException rejected)
                    {
                        failed = new Outcome(rejected.Reason.ToString(), ClinicalAiFailure.InvalidStructuredOutput, null, null);
                    }
                    catch (ClinicalAiOutputException output)
                    {
                        transmitted = true;
                        usage = Add(usage, output.Usage);
                        failed = new Outcome("InvalidOutput", ClinicalAiFailure.InvalidStructuredOutput, null, null);
                    }
                    catch (ClinicalAiProviderException provider)
                    {
                        transmitted |= !provider.BeforeTransmission;
                        usage = Add(usage, provider.Usage);
                        failed = new Outcome("ProviderUnavailable", provider.Failure, null, null);
                    }
                    catch (Exception exception)
                    {
                        // Any other failure of the provider boundary: only its type is logged, never its message.
                        _logger.LogWarning("Clinical AI provider failed with {ExceptionType}.", exception.GetType().Name);
                        transmitted = true;
                        failed = new Outcome("ProviderUnavailable", ClinicalAiFailure.ProviderUnavailable, null, null);
                    }

                    // Only an unreadable or rejected answer is worth one regeneration, and only with time left for it.
                    var elapsed = _time.GetElapsedTime(started);
                    if (failed.Category == ClinicalAiFailure.InvalidStructuredOutput && calls < MaxProviderCalls && elapsed < _ai.Timeout / 2)
                    {
                        continue;
                    }

                    throw await FailAsync(
                        context, failed with { Latency = Latency(elapsed), Calls = calls }, prepared, cancellationToken, Export(transmitted), usage);
                }
            }

            var duration = _time.GetElapsedTime(started);
            var latency = Latency(duration);
            var now = _time.GetUtcNow();
            _writes.Add(ClinicalAuditEntry.Success(
                context.Actor.AuditActor, Export(transmitted), EntityName, context.EncounterId.ToString(), now,
                new AuditContext(context.PatientId, context.EncounterId,
                    ChangesJson: Changes(prepared, new Outcome(null, null, latency, calls), cacheHit: false, usage))));
            await _writes.SaveChangesAsync(cancellationToken);
            Log("Success", prepared, latency);
            _metrics.Request(_ai, Operation, "Success");
            _metrics.Duration(_ai, Operation, "Success", duration);
            _metrics.Usage(_ai, Operation, usage);

            var summary = new EncounterAiSummary(
                AiGenerated: true,
                RequiresClinicianReview: true,
                accepted.Summary,
                accepted.Sections,
                accepted.ReviewPoints,
                accepted.NotDocumented,
                now,
                _ai.Provider,
                _ai.ModelVersion,
                _ai.PromptVersion,
                prepared.Fingerprint,
                prepared.Input.Truncated);
            if (_ai.SendsDataExternally)
            {
                _cache.Set(key, summary, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = CacheTimeToLive });
            }

            return summary;
        }

        private sealed record Attempt(ClinicalActor Actor, Guid EncounterId, Guid? PatientId);

        // Everything a cached summary depends on: a different user, encounter, input, prompt, provider or model is a miss.
        private sealed record CacheKey(
            Guid UserId, Guid EncounterId, string SourceFingerprint, string PromptVersion, string Provider, string ModelVersion);

        // Failure (foundation names: ProviderDisabled, Timeout, InvalidOutput, UnknownSource, ProviderUnavailable), its
        // provider category, latency category and provider calls.
        private sealed record Outcome(string? Failure, ClinicalAiFailure? Category, string? Latency, int? Calls);

        private AuditActionType Export(bool transmitted) =>
            _ai.SendsDataExternally && transmitted ? AuditActionType.Export : AuditActionType.Read;

        private static ClinicalAiUsage? Add(ClinicalAiUsage? total, ClinicalAiUsage? usage) => total?.Add(usage) ?? usage;

        // A validated summary of this user, encounter, input, prompt, provider and model from a moment ago: returned as it
        // was (same GeneratedAtUtc), audited as a Read without any new provider call or usage.
        private async Task<EncounterAiSummary> CacheHitAsync(
            Attempt context, EncounterAiPreparedInput prepared, EncounterAiSummary cached, CancellationToken cancellationToken)
        {
            _writes.Add(ClinicalAuditEntry.Success(
                context.Actor.AuditActor, AuditActionType.Read, EntityName, context.EncounterId.ToString(), _time.GetUtcNow(),
                new AuditContext(context.PatientId, context.EncounterId,
                    ChangesJson: Changes(prepared, new Outcome(null, null, null, 0), cacheHit: true, usage: null))));
            await _writes.SaveChangesAsync(cancellationToken);
            Log("CacheHit", prepared, latency: null);
            _metrics.Request(_ai, Operation, "CacheHit");
            _metrics.CacheHit(_ai, Operation);
            return cached;
        }

        // Records the failed request and returns the generic 503 to throw.
        private async Task<ExternalServiceUnavailableException> FailAsync(
            Attempt context, Outcome outcome, EncounterAiPreparedInput? prepared, CancellationToken cancellationToken,
            AuditActionType action = AuditActionType.Read, ClinicalAiUsage? usage = null)
        {
            var failure = outcome.Failure!;
            _writes.Add(ClinicalAuditEntry.Failure(
                context.Actor.AuditActor, action, EntityName, context.EncounterId.ToString(), _time.GetUtcNow(), failure,
                new AuditContext(context.PatientId, context.EncounterId, ChangesJson: Changes(prepared, outcome, cacheHit: false, usage))));
            await _writes.SaveChangesAsync(cancellationToken);
            Log(failure, prepared, outcome.Latency);
            var category = outcome.Category?.ToString() ?? failure;
            _metrics.Request(_ai, Operation, category);
            _metrics.Failure(_ai, Operation, category);
            _metrics.Usage(_ai, Operation, usage);
            return new ExternalServiceUnavailableException(Unavailable);
        }

        private string Changes(EncounterAiPreparedInput? prepared, Outcome outcome, bool cacheHit, ClinicalAiUsage? usage) =>
            JsonSerializer.Serialize(new
            {
                operation = Operation,
                provider = _ai.Provider,
                model = _ai.ModelVersion,
                promptVersion = _ai.PromptVersion,
                sourceFingerprint = prepared?.Fingerprint,
                records = prepared?.Counts,
                truncated = prepared?.Input.Truncated,
                latency = outcome.Latency,
                failure = outcome.Failure,
                category = outcome.Category?.ToString(),
                providerCalls = outcome.Calls ?? 0,
                cacheHit,
                usage
            }, _auditJson);

        // Safe metadata only (the request's correlation id comes from the logging scope).
        private void Log(string outcome, EncounterAiPreparedInput? prepared, string? latency) =>
            _logger.LogInformation(
                "Clinical AI {Operation} via {Provider}/{Model} ({PromptVersion}): {Outcome}, latency {Latency}, {RecordCount} records, truncated {Truncated}.",
                Operation, _ai.Provider, _ai.ModelVersion, _ai.PromptVersion, outcome, latency ?? "none",
                prepared is null ? 0 : prepared.Sources.Count, prepared?.Input.Truncated ?? false);

        private static string Latency(TimeSpan elapsed) => elapsed.TotalSeconds switch
        {
            < 2 => "Under2s",
            < 5 => "Under5s",
            < 15 => "Under15s",
            _ => "Over15s"
        };
    }
}
