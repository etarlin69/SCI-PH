#pragma once

#include <cstdint>

#include "core/clock.h"
#include "core/console_output.h"

namespace sciph {

// What the person at the reader sees or hears. Never patient information.
enum class FeedbackSignal {
    Ready,
    Provisioning,
    PlaceFinger,
    RemoveFinger,
    PlaceAgain,
    Success,
    NotRecognized,
    FingersDoNotMatch,
    LowQuality,
    SensorError,
    SensorFull,
    NetworkError,
    AuthenticationFailed
};

class IFeedback {
public:
    virtual ~IFeedback() = default;
    virtual void signal(FeedbackSignal signal) = 0;
};

enum class LedMode { Off, On, BlinkSlow, BlinkFast };

// Optional LED and buzzer (GPIO on the ESP32).
class IIndicator {
public:
    virtual ~IIndicator() = default;
    virtual void setLed(bool on) = 0;
    virtual void beep(uint16_t durationMs) = 0;
};

const char* feedbackMessage(FeedbackSignal signal);
LedMode feedbackLedMode(FeedbackSignal signal);

// Serial message + LED pattern + beeps. Authentication failure keeps the LED steady on until another state.
class FeedbackController : public IFeedback {
public:
    FeedbackController(IConsoleOutput& output, IIndicator* indicator, IClock& clock)
        : output_(output), indicator_(indicator), clock_(clock) {}

    void signal(FeedbackSignal signal) override;
    // Drives the blink patterns; call from the main loop.
    void tick();
    LedMode ledMode() const { return mode_; }
    FeedbackSignal lastSignal() const { return last_; }

private:
    IConsoleOutput& output_;
    IIndicator* indicator_;
    IClock& clock_;
    LedMode mode_ = LedMode::Off;
    FeedbackSignal last_ = FeedbackSignal::Ready;
    bool ledOn_ = false;
};

}  // namespace sciph
