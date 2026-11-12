#pragma once

#if defined(ARDUINO)

#include "core/clock.h"
#include "core/console_output.h"
#include "core/uuid.h"

namespace sciph {

// esp_timer (64-bit, no 49-day wrap) and the SNTP-set system time.
class SystemClock : public IClock {
public:
    uint64_t monotonicMs() const override;
    bool utcNow(int64_t& epochSeconds) const override;
    void sleepMs(uint32_t ms) override;
};

// esp_random(): the hardware RNG (true random while the radio is on).
class HardwareRandom : public IRandomSource {
public:
    uint32_t next32() override;
};

class SerialOutput : public IConsoleOutput {
public:
    void println(const std::string& line) override;
};

}  // namespace sciph

#endif
