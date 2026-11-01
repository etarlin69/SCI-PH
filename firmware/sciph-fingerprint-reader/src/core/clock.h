#pragma once

#include <cstdint>

namespace sciph {

// Monotonic milliseconds drive every schedule; UTC is only reported once SNTP produced a plausible time.
class IClock {
public:
    virtual ~IClock() = default;
    virtual uint64_t monotonicMs() const = 0;
    virtual bool utcNow(int64_t& epochSeconds) const = 0;
    virtual void sleepMs(uint32_t ms) = 0;
};

// 2024-01-01T00:00:00Z: an earlier clock means SNTP has not set the time yet.
constexpr int64_t kMinimumPlausibleEpoch = 1704067200;

inline bool isPlausibleUtc(int64_t epochSeconds) { return epochSeconds >= kMinimumPlausibleEpoch; }

}  // namespace sciph
