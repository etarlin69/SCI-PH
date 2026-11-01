#pragma once

#if defined(ARDUINO)

#include "feedback/feedback.h"

namespace sciph {

// Optional LED and active buzzer on GPIO (-1 = not connected).
class GpioIndicator : public IIndicator {
public:
    GpioIndicator(int ledPin, int buzzerPin, bool ledActiveHigh)
        : ledPin_(ledPin), buzzerPin_(buzzerPin), ledActiveHigh_(ledActiveHigh) {}

    void begin();
    void setLed(bool on) override;
    void beep(uint16_t durationMs) override;

private:
    int ledPin_;
    int buzzerPin_;
    bool ledActiveHigh_;
};

}  // namespace sciph

#endif
