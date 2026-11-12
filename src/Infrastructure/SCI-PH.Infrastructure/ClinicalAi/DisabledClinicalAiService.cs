using SCI_PH.Application.Abstractions.AI;

namespace SCI_PH.Infrastructure.ClinicalAi
{
    // No clinical AI provider configured (Production until a real provider is set up): Application answers 503 without
    // calling it.
    public sealed class DisabledClinicalAiService : IClinicalAiService
    {
        public string Provider => "SCIPH-AiDisabled";

        public string ModelVersion => "none";

        public string PromptVersion => "none";

        public TimeSpan Timeout => TimeSpan.Zero;

        public bool IsEnabled => false;

        public Task<EncounterSummaryDraft> SummarizeEncounterAsync(EncounterSummaryInput input, CancellationToken cancellationToken) =>
            throw new ClinicalAiProviderException("The clinical AI provider is disabled.");
    }
}
