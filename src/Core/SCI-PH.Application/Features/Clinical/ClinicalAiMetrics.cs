using System.Diagnostics.Metrics;
using SCI_PH.Application.Abstractions.AI;

namespace SCI_PH.Application.Features.Clinical
{
    // Meter "SCI-PH.ClinicalAi" (ADR-014): requests, failures, duration, tokens and cache hits of the assisted clinical AI.
    // Tags are low-cardinality server values only (provider, configured model version, operation, outcome or failure
    // category): never a user, encounter, patient, fingerprint, prompt or any content.
    public sealed class ClinicalAiMetrics : IDisposable
    {
        public const string MeterName = "SCI-PH.ClinicalAi";

        private readonly Counter<long> _requests;
        private readonly Counter<long> _failures;
        private readonly Histogram<double> _duration;
        private readonly Counter<long> _inputTokens;
        private readonly Counter<long> _outputTokens;
        private readonly Counter<long> _cacheHits;

        public ClinicalAiMetrics()
        {
            Meter = new Meter(MeterName);
            _requests = Meter.CreateCounter<long>("ai_requests_total", description: "Assisted clinical AI requests, by outcome.");
            _failures = Meter.CreateCounter<long>("ai_failures_total", description: "Failed assisted clinical AI requests, by failure category.");
            _duration = Meter.CreateHistogram<double>("ai_request_duration", unit: "s", description: "Duration of the provider calls of one request.");
            _inputTokens = Meter.CreateCounter<long>("ai_input_tokens", unit: "{token}", description: "Input tokens billed by the provider.");
            _outputTokens = Meter.CreateCounter<long>("ai_output_tokens", unit: "{token}", description: "Output tokens (reasoning included) billed by the provider.");
            _cacheHits = Meter.CreateCounter<long>("ai_cache_hits_total", description: "Requests answered from the in-memory summary cache.");
        }

        public Meter Meter { get; }

        public void Request(IClinicalAiService ai, string operation, string outcome) =>
            _requests.Add(1, Tags(ai, operation, outcome));

        public void Failure(IClinicalAiService ai, string operation, string failure) =>
            _failures.Add(1, Tags(ai, operation, failure));

        public void Duration(IClinicalAiService ai, string operation, string outcome, TimeSpan duration) =>
            _duration.Record(duration.TotalSeconds, Tags(ai, operation, outcome));

        public void Usage(IClinicalAiService ai, string operation, ClinicalAiUsage? usage)
        {
            if (usage is null)
            {
                return;
            }

            _inputTokens.Add(usage.InputTokens, Tags(ai, operation, outcome: null));
            _outputTokens.Add(usage.OutputTokens, Tags(ai, operation, outcome: null));
        }

        public void CacheHit(IClinicalAiService ai, string operation) =>
            _cacheHits.Add(1, Tags(ai, operation, outcome: null));

        public void Dispose() => Meter.Dispose();

        private static KeyValuePair<string, object?>[] Tags(IClinicalAiService ai, string operation, string? outcome) => outcome is null
            ? [new("provider", ai.Provider), new("model", ai.ModelVersion), new("operation", operation)]
            : [new("provider", ai.Provider), new("model", ai.ModelVersion), new("operation", operation), new("outcome", outcome)];
    }
}
