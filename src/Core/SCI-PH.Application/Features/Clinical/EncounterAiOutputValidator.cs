using System.Text.RegularExpressions;
using SCI_PH.Application.Abstractions.AI;
using SCI_PH.Application.DTOs.Clinical;

namespace SCI_PH.Application.Features.Clinical
{
    // Why a provider draft was rejected (audit/log category; never shown to a client).
    public enum EncounterAiRejection
    {
        InvalidOutput = 1,
        UnknownSource = 2
    }

    public sealed class EncounterAiOutputRejectedException : Exception
    {
        public EncounterAiOutputRejectedException(EncounterAiRejection reason, string message) : base(message) => Reason = reason;

        public EncounterAiRejection Reason { get; }
    }

    // A provider's draft is never trusted (ADR-013). It is accepted only whole: every field present, every text non-blank,
    // plain text (no HTML tag, no control character but line breaks) and within its limit, kinds and source types exactly
    // one of the published names, and every source an alias of THIS input of the stated type; model and prompt version
    // must be the provider's own. Anything else rejects the whole draft: nothing partial reaches a client.
    public static class EncounterAiOutputValidator
    {
        public const int MaxSummary = 1500;
        public const int MaxSection = 800;
        public const int MaxReviewPointText = 300;
        public const int MaxReviewPoints = 10;
        public const int MaxNotDocumented = 10;
        public const int MaxNotDocumentedText = 200;
        public const int MaxSourcesPerItem = 20;

        // An HTML/XML tag, comment or declaration start ("<b", "</p", "<!--", "<?xml"); "< 90" or "<90" stay valid text.
        private static readonly Regex _markup = new(@"<\s*[/!?]?\s*[A-Za-z]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        public static (string Summary, EncounterAiSections Sections, IReadOnlyList<EncounterAiReviewPoint> ReviewPoints, IReadOnlyList<string> NotDocumented) Validate(
            EncounterSummaryDraft draft, EncounterAiPreparedInput prepared, string expectedModel, string expectedPromptVersion)
        {
            if (!string.Equals(draft.Model, expectedModel, StringComparison.Ordinal)
                || !string.Equals(draft.PromptVersion, expectedPromptVersion, StringComparison.Ordinal))
            {
                throw Invalid("The draft does not carry the provider's model and prompt version.");
            }

            var summary = Text(draft.Summary, MaxSummary, "summary");
            var sections = draft.Sections ?? throw Invalid("The draft has no sections object.");
            var reviewPoints = draft.ReviewPoints ?? throw Invalid("The draft has no review points list.");
            var notDocumented = draft.NotDocumented ?? throw Invalid("The draft has no not-documented list.");
            if (reviewPoints.Count > MaxReviewPoints)
            {
                throw Invalid("The draft has too many review points.");
            }

            if (notDocumented.Count > MaxNotDocumented)
            {
                throw Invalid("The draft has too many not-documented entries.");
            }

            return (
                summary,
                new EncounterAiSections(
                    Section(sections.Course, prepared, "course"),
                    Section(sections.VitalSigns, prepared, "vitalSigns"),
                    Section(sections.Medications, prepared, "medications"),
                    Section(sections.Procedures, prepared, "procedures"),
                    Section(sections.Notes, prepared, "notes"),
                    Section(sections.Transfer, prepared, "transfer")),
                reviewPoints.Select((point, i) => ReviewPoint(point, prepared, i)).ToList(),
                notDocumented.Select((entry, i) => Text(entry, MaxNotDocumentedText, $"notDocumented[{i}]")).ToList());
        }

        private static EncounterAiSection? Section(SectionDraft? section, EncounterAiPreparedInput prepared, string field) =>
            section is null ? null : new EncounterAiSection(Text(section.Text, MaxSection, field), Sources(section.Sources, prepared, field));

        private static EncounterAiReviewPoint ReviewPoint(ReviewPointDraft? point, EncounterAiPreparedInput prepared, int index)
        {
            var field = $"reviewPoints[{index}]";
            if (point is null)
            {
                throw Invalid($"{field} is missing.");
            }

            var kind = Name<EncounterAiReviewPointKind>(point.Kind) ?? throw Invalid($"{field} has an unknown kind.");
            return new EncounterAiReviewPoint(kind, Text(point.Text, MaxReviewPointText, field), Sources(point.Sources, prepared, field));
        }

        // Every source must name a record of this input with its own type; duplicates collapse.
        private static IReadOnlyList<EncounterAiSourceReference> Sources(
            IReadOnlyList<SourceDraft>? sources, EncounterAiPreparedInput prepared, string field)
        {
            if (sources is null)
            {
                throw Invalid($"{field} has no sources list.");
            }

            if (sources.Count > MaxSourcesPerItem)
            {
                throw Invalid($"{field} has too many sources.");
            }

            var references = new List<EncounterAiSourceReference>();
            foreach (var source in sources)
            {
                var type = Name<ClinicalAiSourceType>(source?.Type)
                    ?? throw new EncounterAiOutputRejectedException(EncounterAiRejection.UnknownSource, $"{field} references an unknown record type.");
                if (source!.Alias is not { } alias
                    || !prepared.Sources.TryGetValue(alias, out var record)
                    || record.Type != type)
                {
                    throw new EncounterAiOutputRejectedException(EncounterAiRejection.UnknownSource, $"{field} references a record that is not in the input.");
                }

                var reference = new EncounterAiSourceReference(record.Type, record.Id);
                if (!references.Contains(reference))
                {
                    references.Add(reference);
                }
            }

            return references;
        }

        private static string Text(string? value, int maxLength, string field)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                throw Invalid($"{field} is empty.");
            }

            if (text.Length > maxLength)
            {
                throw Invalid($"{field} is longer than {maxLength} characters.");
            }

            if (text.Any(c => char.IsControl(c) && c != '\n') || _markup.IsMatch(text))
            {
                throw Invalid($"{field} is not plain text.");
            }

            return text;
        }

        // Exactly a published member name (case-sensitive); numbers and unknown names are rejected.
        private static TEnum? Name<TEnum>(string? value) where TEnum : struct, Enum =>
            value is not null && Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal) ? Enum.Parse<TEnum>(value) : null;

        private static EncounterAiOutputRejectedException Invalid(string message) => new(EncounterAiRejection.InvalidOutput, message);
    }
}
