using Microsoft.Extensions.Logging;
using SCI_PH.Application.Abstractions.AI;

namespace SCI_PH.Application.Tests.Clinical
{
    // A clinical AI provider for the Application tests: records what it received and answers what the test decides
    // (by default a minimal valid draft that references the first record of each type).
    internal sealed class TestClinicalAiProvider : IClinicalAiService
    {
        public const string Model = "test-model";
        public const string Prompt = "encounter-summary.test";

        public string Provider => "SCIPH-AiTest";

        public string ModelVersion => Model;

        public string PromptVersion => Prompt;

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        public bool IsEnabled { get; set; } = true;

        public List<EncounterSummaryInput> Inputs { get; } = [];

        // Answers the draft for the input; may throw (provider failures) or wait (timeout, cancellation).
        public Func<EncounterSummaryInput, CancellationToken, Task<EncounterSummaryDraft>> Answer { get; set; } =
            (input, _) => Task.FromResult(ValidDraft(input));

        public async Task<EncounterSummaryDraft> SummarizeEncounterAsync(EncounterSummaryInput input, CancellationToken cancellationToken)
        {
            Inputs.Add(input);
            return await Answer(input, cancellationToken);
        }

        public static EncounterSummaryDraft ValidDraft(EncounterSummaryInput input, IReadOnlyList<ReviewPointDraft>? reviewPoints = null)
        {
            SectionDraft? Section(string type, string? alias) => alias is null ? null : new SectionDraft($"Registro {alias}.", [new SourceDraft(type, alias)]);
            return new EncounterSummaryDraft(
                "Resumen de la atención.",
                new EncounterSummarySectionsDraft(
                    Section("Triage", input.Triages.FirstOrDefault()?.Alias),
                    Section("VitalSign", input.VitalSigns.FirstOrDefault()?.Alias),
                    Section("MedicationAdministration", input.Administrations.FirstOrDefault()?.Alias),
                    Section("ClinicalProcedure", input.Procedures.FirstOrDefault()?.Alias),
                    Section("ClinicalNote", input.Notes.FirstOrDefault()?.Alias),
                    Section("ClinicalTransfer", input.Transfers.FirstOrDefault()?.Alias)),
                reviewPoints ?? [],
                ["Procedimientos"],
                Model,
                Prompt);
        }
    }

    // Every log line written through the logger, formatted, for leak checks.
    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception is null ? string.Empty : $" {exception}"));
    }
}
