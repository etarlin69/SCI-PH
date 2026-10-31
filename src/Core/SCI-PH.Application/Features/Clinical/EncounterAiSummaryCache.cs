using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using SCI_PH.Application.DTOs.Clinical;

namespace SCI_PH.Application.Features.Clinical
{
    // Short-lived deduplication of external clinical AI summaries (ADR-014): the same user asking again for the same
    // encounter, with exactly the same minimized input (source fingerprint), prompt, provider and model, gets the summary
    // already validated a moment ago instead of a second external call (cost, data transfer). It is NOT idempotency: a
    // restart or another instance simply calls the provider again.
    // Privacy: process memory only (no database, Redis, Blob or disk; never serialized, logged or dumped), its own bounded
    // MemoryCache (not the host's), keyed per user so one user's entry never answers another, every entry expiring
    // 10 minutes after it was stored. Only validated summaries enter it, never a failure.
    public sealed class EncounterAiSummaryCache : IDisposable
    {
        public const int SizeLimit = 500;
        public static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(10);

        private readonly MemoryCache _cache;

        public EncounterAiSummaryCache(TimeProvider time) =>
            _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = SizeLimit, Clock = new Clock(time) });

        public bool TryGet(EncounterAiSummaryCacheKey key, out EncounterAiSummary summary) =>
            _cache.TryGetValue(key, out summary!);

        public void Set(EncounterAiSummaryCacheKey key, EncounterAiSummary summary) =>
            _cache.Set(key, summary, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeToLive });

        public void Dispose() => _cache.Dispose();

        // MemoryCache reads expiration against this clock, so tests can move time.
        private sealed class Clock(TimeProvider time) : ISystemClock
        {
            public DateTimeOffset UtcNow => time.GetUtcNow();
        }
    }

    // Everything an entry depends on: a different user, encounter, input, prompt, provider or model is a miss.
    public sealed record EncounterAiSummaryCacheKey(
        Guid UserId, Guid EncounterId, string SourceFingerprint, string PromptVersion, string Provider, string ModelVersion);
}
