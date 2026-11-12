namespace SCI_PH.Application.Abstractions.AI
{
    // Assisted clinical AI (ADR-013): CLOSED, read-only operations only. There is no free prompt, question or chat: the
    // provider receives a typed, minimized input built by Application and answers a typed draft that Application validates
    // before anything reaches a client. The provider owns its prompt (one versioned template) and its model; a client never
    // chooses either. Implementations: Fake (deterministic, in process), Disabled (503) and Azure OpenAI (external, ADR-014).
    public interface IClinicalAiService
    {
        string Provider { get; }

        string ModelVersion { get; }

        // The version of the provider's prompt template ("encounter-summary.v1"); every draft must carry it.
        string PromptVersion { get; }

        // Upper bound of one operation; Application cancels it after that (503).
        TimeSpan Timeout { get; }

        // False only for the Disabled provider: no operation is attempted (503).
        bool IsEnabled => true;

        // True when an operation transmits its input outside SCI-PH (a cloud model, ADR-014): such a host starts only with
        // ClinicalAi:AllowExternalClinicalData, every attempt is audited as an Export and identical repeated requests are
        // answered from a short in-memory cache. False for the in-process Fake and for Disabled.
        bool SendsDataExternally => false;

        // Summary and documentation review points of ONE encounter, from the supplied data only. Throws
        // ClinicalAiProviderException when the provider is unavailable and ClinicalAiOutputException when its answer cannot
        // be read; honors the cancellation token.
        Task<EncounterSummaryDraft> SummarizeEncounterAsync(EncounterSummaryInput input, CancellationToken cancellationToken);
    }

    // The only data a provider receives for an encounter summary. Minimized by construction: no patient, clinic or staff
    // identity, no record ids (each record has an alias such as "V3", mapped back to its id only inside Application) and
    // no absolute instants (times are relative to the encounter start, "+01:10"). Enumerations travel as their names.
    public sealed record EncounterSummaryInput(
        EncounterOverviewInput Encounter,
        IReadOnlyList<TriageInput> Triages,
        IReadOnlyList<VitalSignInput> VitalSigns,
        IReadOnlyList<ClinicalNoteInput> Notes,
        IReadOnlyList<DiagnosisInput> Diagnoses,
        IReadOnlyList<PrescriptionInput> Prescriptions,
        IReadOnlyList<MedicationAdministrationInput> Administrations,
        IReadOnlyList<ProcedureInput> Procedures,
        IReadOnlyList<TransferInput> Transfers,
        bool Truncated);

    public sealed record EncounterOverviewInput(string Type, string Status, string? Reason, string? LatestTriagePriority, string? EndedAt);

    public sealed record TriageInput(string Alias, string At, string Priority, string? Reason, string? Observations);

    public sealed record VitalSignInput(string Alias, string At, string Type, decimal Value, string Unit, string? TriageAlias);

    public sealed record ClinicalNoteInput(string Alias, string At, string Type, string Content);

    public sealed record DiagnosisInput(
        string Alias, string At, string Type, string Description, string? CodeSystem, string? Code, bool IsPrimary, bool Confirmed);

    public sealed record PrescriptionInput(
        string Alias, string At, string Status, string? CancelledAt, string? Notes, IReadOnlyList<PrescriptionLineInput> Lines);

    public sealed record PrescriptionLineInput(
        string MedicationName, decimal Dose, string DoseUnit, string Route, string Frequency, int? DurationDays, string? Instructions);

    public sealed record MedicationAdministrationInput(
        string Alias, string At, string MedicationName, decimal Dose, string DoseUnit, string Route, string Status,
        string? PrescriptionAlias, string? Notes);

    public sealed record ProcedureInput(string Alias, string? At, string Name, string? Code, string Status, string? Outcome);

    public sealed record TransferInput(
        string Alias, string Status, string RequestedAt, string? SentAt, string? ReceivedAt, string? CancelledAt, string? Reason, string? Notes);

    // What a provider answers, NOT trusted: kinds and source types are plain strings and every field may be missing or
    // too long until Application's validator accepts it. Model and PromptVersion are set by the provider code, never by
    // the model's text. Usage is the provider's token count for the call (null when it has none, as the Fake).
    public sealed record EncounterSummaryDraft(
        string? Summary,
        EncounterSummarySectionsDraft? Sections,
        IReadOnlyList<ReviewPointDraft>? ReviewPoints,
        IReadOnlyList<string>? NotDocumented,
        string Model,
        string PromptVersion,
        ClinicalAiUsage? Usage = null);

    // Tokens billed for one provider call: safe metadata for the audit, the metrics and cost, never shown to a client.
    // ReasoningTokens (part of OutputTokens) only when the provider reports them.
    public sealed record ClinicalAiUsage(int InputTokens, int OutputTokens, int? ReasoningTokens = null)
    {
        public ClinicalAiUsage Add(ClinicalAiUsage? other) => other is null
            ? this
            : new(InputTokens + other.InputTokens, OutputTokens + other.OutputTokens,
                ReasoningTokens is null && other.ReasoningTokens is null ? null : (ReasoningTokens ?? 0) + (other.ReasoningTokens ?? 0));
    }

    // Why a provider call failed, for the audit and the metrics only: every category is the same generic 503 for a client.
    public enum ClinicalAiFailure
    {
        Timeout = 1,
        // The provider's own rate limit (requests or tokens per minute); our limiter's 429 is separate.
        RateLimitedByProvider = 2,
        QuotaExceeded = 3,
        AuthenticationFailed = 4,
        // A content filter on the input or the answer, or a refusal: legitimate clinical text (trauma, overdose, self-harm,
        // violence) can trigger it, so it says nothing about the input being malicious.
        ContentFiltered = 5,
        InvalidStructuredOutput = 6,
        ProviderUnavailable = 7,
        // The provider rejected the request itself (deployment, schema, parameters, input size): fixed by configuration.
        ConfigurationError = 8
    }

    public sealed record EncounterSummarySectionsDraft(
        SectionDraft? Course,
        SectionDraft? VitalSigns,
        SectionDraft? Medications,
        SectionDraft? Procedures,
        SectionDraft? Notes,
        SectionDraft? Transfer);

    public sealed record SectionDraft(string? Text, IReadOnlyList<SourceDraft>? Sources);

    public sealed record ReviewPointDraft(string? Kind, string? Text, IReadOnlyList<SourceDraft>? Sources);

    public sealed record SourceDraft(string? Type, string? Alias);

    // The provider could not answer (unreachable, failed, refused). The message is internal and never reaches a client.
    // BeforeTransmission: the input never left SCI-PH (rejected locally), so the attempt is not an export.
    public sealed class ClinicalAiProviderException : Exception
    {
        public ClinicalAiProviderException(string message, Exception? innerException = null)
            : this(ClinicalAiFailure.ProviderUnavailable, message, innerException) { }

        public ClinicalAiProviderException(
            ClinicalAiFailure failure, string message, Exception? innerException = null, bool beforeTransmission = false, ClinicalAiUsage? usage = null)
            : base(message, innerException)
        {
            Failure = failure;
            BeforeTransmission = beforeTransmission;
            Usage = usage;
        }

        public ClinicalAiFailure Failure { get; }

        public bool BeforeTransmission { get; }

        // Tokens the provider billed before failing (a filtered or refused answer), if it reported them.
        public ClinicalAiUsage? Usage { get; }
    }

    // The provider answered something that is not a valid draft (not JSON, wrong shape, cut at its length limit). Never
    // shown to a client. Usage: the tokens that answer cost, if reported.
    public sealed class ClinicalAiOutputException : Exception
    {
        public ClinicalAiOutputException(string message, Exception? innerException = null, ClinicalAiUsage? usage = null)
            : base(message, innerException) => Usage = usage;

        public ClinicalAiUsage? Usage { get; }
    }
}
