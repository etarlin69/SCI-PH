using System.Text.Json.Serialization;
using SCI_PH.Application.Common.Serialization;

namespace SCI_PH.Application.DTOs.Clinical
{
    // POST /api/web/encounters/{id}/ai-summary takes no options: the operation, provider, model, prompt and limits are
    // server-side only. An empty body or {} is accepted; any member (model, prompt, temperature, provider...) is a 400.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GenerateEncounterAiSummaryRequest;

    // Kinds of documentation review point (closed set): a gap in the documentation, records that contradict each other,
    // and a documented item that has no documented follow-up. Never a clinical judgment, severity or recommendation.
    public enum EncounterAiReviewPointKind
    {
        MissingDocumentation = 1,
        Inconsistency = 2,
        PendingItem = 3
    }

    // Record types that a summary may reference (each one a record of the summarized encounter).
    public enum ClinicalAiSourceType
    {
        Triage = 1,
        VitalSign = 2,
        ClinicalNote = 3,
        Diagnosis = 4,
        Prescription = 5,
        MedicationAdministration = 6,
        ClinicalProcedure = 7,
        ClinicalTransfer = 8
    }

    // An AI-generated, ephemeral summary of one encounter (ADR-013): it requires clinician review, is never stored and is
    // a snapshot of the records at GeneratedAtUtc. Sources are "referenced records" for navigation: their existence is
    // checked, NOT that the sentence is supported by them. No diagnosis, recommendation, treatment, severity or confidence.
    public sealed record EncounterAiSummary(
        bool AiGenerated,
        bool RequiresClinicianReview,
        string Summary,
        EncounterAiSections Sections,
        IReadOnlyList<EncounterAiReviewPoint> ReviewPoints,
        IReadOnlyList<string> NotDocumented,
        DateTimeOffset GeneratedAtUtc,
        string Provider,
        string Model,
        string PromptVersion,
        string SourceFingerprint,
        bool Truncated);

    public sealed record EncounterAiSections(
        EncounterAiSection? Course,
        EncounterAiSection? VitalSigns,
        EncounterAiSection? Medications,
        EncounterAiSection? Procedures,
        EncounterAiSection? Notes,
        EncounterAiSection? Transfer);

    public sealed record EncounterAiSection(string Text, IReadOnlyList<EncounterAiSourceReference> Sources);

    public sealed record EncounterAiReviewPoint(
        [property: JsonConverter(typeof(TextEnumConverter))] EncounterAiReviewPointKind Kind,
        string Text,
        IReadOnlyList<EncounterAiSourceReference> Sources);

    public sealed record EncounterAiSourceReference(
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalAiSourceType Type,
        Guid Id);
}
