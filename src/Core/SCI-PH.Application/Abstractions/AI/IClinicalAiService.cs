namespace SCI_PH.Application.Abstractions.AI
{
    // Assisted clinical AI (ADR-013): CLOSED, read-only operations only. There is no free prompt, question or chat: the
    // provider receives a typed, minimized input built by Application and answers a typed draft that Application validates
    // before anything reaches a client. The provider owns its prompt (one versioned template) and its model; a client never
    // chooses either. Implementations: Fake (deterministic, in process), Disabled (503); a real provider is PR 2.
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
    // the model's text.
    public sealed record EncounterSummaryDraft(
        string? Summary,
        EncounterSummarySectionsDraft? Sections,
        IReadOnlyList<ReviewPointDraft>? ReviewPoints,
        IReadOnlyList<string>? NotDocumented,
        string Model,
        string PromptVersion);

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
    public sealed class ClinicalAiProviderException : Exception
    {
        public ClinicalAiProviderException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    // The provider answered something that is not a valid draft (not JSON, wrong shape). Never shown to a client.
    public sealed class ClinicalAiOutputException : Exception
    {
        public ClinicalAiOutputException(string message, Exception? innerException = null) : base(message, innerException) { }
    }
}
