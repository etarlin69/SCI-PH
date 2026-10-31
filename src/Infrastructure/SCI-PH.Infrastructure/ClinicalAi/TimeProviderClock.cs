using Microsoft.Extensions.Internal;

namespace SCI_PH.Infrastructure.ClinicalAi
{
    // The clinical AI summary cache reads expirations on the host's TimeProvider (the same clock as the rest of SCI-PH).
    internal sealed class TimeProviderClock(TimeProvider time) : ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
}
