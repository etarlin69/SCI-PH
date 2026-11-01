#pragma once

#include <cstdint>

#include "config/reader_settings.h"
#include "core/clock.h"
#include "feedback/feedback.h"
#include "fingerprint/r307/fingerprint_sensor.h"

namespace sciph {

enum class CaptureResult { Captured, Timeout, LowQuality, SensorError };

// Waits for a finger, takes the image and turns it into a character file in CharBuffer `buffer`. Poor captures are
// retried locally (the finger is lifted and placed again) up to maxLowQualityAttempts.
CaptureResult captureFinger(IFingerprintSensor& sensor, IClock& clock, IFeedback& feedback, uint8_t buffer,
                            FeedbackSignal prompt, const CaptureSettings& settings);

enum class RemovalResult { Removed, Timeout, SensorError };

RemovalResult waitForFingerRemoved(IFingerprintSensor& sensor, IClock& clock, const CaptureSettings& settings);

}  // namespace sciph
