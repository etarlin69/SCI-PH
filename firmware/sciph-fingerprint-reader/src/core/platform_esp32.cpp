#if defined(ARDUINO)

#include "core/platform_esp32.h"

#include <Arduino.h>
#include <esp_random.h>
#include <esp_timer.h>
#include <time.h>

namespace sciph {

uint64_t SystemClock::monotonicMs() const { return static_cast<uint64_t>(esp_timer_get_time() / 1000); }

bool SystemClock::utcNow(int64_t& epochSeconds) const {
    const time_t now = time(nullptr);
    epochSeconds = static_cast<int64_t>(now);
    return isPlausibleUtc(epochSeconds);
}

void SystemClock::sleepMs(uint32_t ms) { delay(ms); }

uint32_t HardwareRandom::next32() { return esp_random(); }

void SerialOutput::println(const std::string& line) { Serial.println(line.c_str()); }

}  // namespace sciph

#endif
