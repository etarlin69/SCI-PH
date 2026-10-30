using System.Text.Json;
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
    // Assisted, read-only summary of one encounter with documentation review points (ADR-013). Same access as reading the
    // encounter (Encounters.Read, an active clinical actor of the encounter's clinic, the patient rule). The provider gets
    // only the minimized input of EncounterAiInputBuilder and its draft is accepted only whole by EncounterAiOutputValidator.
    // The result is ephemeral: it is returned and never stored; nothing clinical is written. The only write is one audit
    // entry per attempt (Read, Success or Failure) with safe metadata: operation, provider, model, prompt version, input
    // fingerprint, record counts, truncation, latency category and failure category; never the input, prompt, answer or
    // any clinical value. Every failure of the provider (Disabled, unavailable, timeout, invalid or unknown-source answer)
    // is the same generic 503. A cancelled request (client gone) records nothing and stops the provider call.
    public sealed class EncounterAiSummaryService
    {
        public const string EntityName = "ClinicalAiEncounterSummary";
        public const string Operation = "encounter-summary";

        private const string Unavailable = "The assisted summary is not available right now. Try again later.";

        private static readonly JsonSerializerOptions _auditJson = new(JsonSerializerDefaults.Web);

        private readonly ICurrentClinicalActor _actor;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly IClinicalAiService _ai;
        private readonly TimeProvider _time;
        private readonly ILogger<EncounterAiSummaryService> _logger;

        public EncounterAiSummaryService(
            ICurrentClinicalActor actor,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IPatientAccessAuthorizer patientAccess,
            IClinicalAiService ai,
            TimeProvider time,
            ILogger<EncounterAiSummaryService> logger)
        {
            _actor = actor;
            _reads = reads;
            _writes = writes;
            _patientAccess = patientAccess;
            _ai = ai;
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
                throw await FailAsync(context, "ProviderDisabled", prepared: null, latency: null, cancellationToken);
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

            var started = _time.GetTimestamp();
            EncounterSummaryDraft draft;
            using (var timeout = new CancellationTokenSource(_ai.Timeout, _time))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                try
                {
                    draft = await _ai.SummarizeEncounterAsync(prepared.Input, linked.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    throw await FailAsync(context, "Timeout", prepared, Latency(started), cancellationToken);
                }
                catch (ClinicalAiOutputException)
                {
                    throw await FailAsync(context, "InvalidOutput", prepared, Latency(started), cancellationToken);
                }
                catch (Exception exception)
                {
                    // Any other failure of the provider boundary: only its type is logged, never its message.
                    _logger.LogWarning("Clinical AI provider failed with {ExceptionType}.", exception.GetType().Name);
                    throw await FailAsync(context, "ProviderUnavailable", prepared, Latency(started), cancellationToken);
                }
            }

            var latency = Latency(started);
            (string Summary, EncounterAiSections Sections, IReadOnlyList<EncounterAiReviewPoint> ReviewPoints, IReadOnlyList<string> NotDocumented) accepted;
            try
            {
                accepted = EncounterAiOutputValidator.Validate(draft, prepared, _ai.ModelVersion, _ai.PromptVersion);
            }
            catch (EncounterAiOutputRejectedException rejected)
            {
                throw await FailAsync(context, rejected.Reason.ToString(), prepared, latency, cancellationToken);
            }

            var now = _time.GetUtcNow();
            _writes.Add(ClinicalAuditEntry.Success(
                context.Actor.AuditActor, AuditActionType.Read, EntityName, context.EncounterId.ToString(), now,
                new AuditContext(context.PatientId, context.EncounterId, ChangesJson: Changes(prepared, latency, failure: null))));
            await _writes.SaveChangesAsync(cancellationToken);
            Log("Success", prepared, latency);

            return new EncounterAiSummary(
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
        }

        private sealed record Attempt(ClinicalActor Actor, Guid EncounterId, Guid? PatientId);

        // Records the failed attempt and returns the generic 503 to throw.
        private async Task<ExternalServiceUnavailableException> FailAsync(
            Attempt context, string failure, EncounterAiPreparedInput? prepared, string? latency, CancellationToken cancellationToken)
        {
            _writes.Add(ClinicalAuditEntry.Failure(
                context.Actor.AuditActor, AuditActionType.Read, EntityName, context.EncounterId.ToString(), _time.GetUtcNow(), failure,
                new AuditContext(context.PatientId, context.EncounterId, ChangesJson: Changes(prepared, latency, failure))));
            await _writes.SaveChangesAsync(cancellationToken);
            Log(failure, prepared, latency);
            return new ExternalServiceUnavailableException(Unavailable);
        }

        private string Changes(EncounterAiPreparedInput? prepared, string? latency, string? failure) =>
            JsonSerializer.Serialize(new
            {
                operation = Operation,
                provider = _ai.Provider,
                model = _ai.ModelVersion,
                promptVersion = _ai.PromptVersion,
                sourceFingerprint = prepared?.Fingerprint,
                records = prepared?.Counts,
                truncated = prepared?.Input.Truncated,
                latency,
                failure
            }, _auditJson);

        // Safe metadata only (the request's correlation id comes from the logging scope).
        private void Log(string outcome, EncounterAiPreparedInput? prepared, string? latency) =>
            _logger.LogInformation(
                "Clinical AI {Operation} via {Provider}/{Model} ({PromptVersion}): {Outcome}, latency {Latency}, {RecordCount} records, truncated {Truncated}.",
                Operation, _ai.Provider, _ai.ModelVersion, _ai.PromptVersion, outcome, latency ?? "none",
                prepared is null ? 0 : prepared.Sources.Count, prepared?.Input.Truncated ?? false);

        private string Latency(long started) => _time.GetElapsedTime(started).TotalSeconds switch
        {
            < 2 => "Under2s",
            < 5 => "Under5s",
            < 15 => "Under15s",
            _ => "Over15s"
        };
    }
}
