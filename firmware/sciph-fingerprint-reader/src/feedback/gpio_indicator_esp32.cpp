#if defined(ARDUINO)

#include "feedback/gpio_indicator_esp32.h"

#include <Arduino.h>

namespace sciph {

void GpioIndicator::begin() {
    if (ledPin_ >= 0) {
        pinMode(ledPin_, OUTPUT);
        setLed(false);
    }
    if (buzzerPin_ >= 0) {
        pinMode(buzzerPin_, OUTPUT);
        digitalWrite(buzzerPin_, LOW);
    }
}

void GpioIndicator::setLed(bool on) {
    if (ledPin_ >= 0) {
        digitalWrite(ledPin_, (on == ledActiveHigh_) ? HIGH : LOW);
    }
}

void GpioIndicator::beep(uint16_t durationMs) {
    if (buzzerPin_ < 0) {
        return;
    }
    digitalWrite(buzzerPin_, HIGH);
    delay(durationMs);
    digitalWrite(buzzerPin_, LOW);
}

}  // namespace sciph

#endif
