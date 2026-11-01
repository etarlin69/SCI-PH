#include "fingerprint/capture/finger_capture.h"

namespace sciph {

namespace {

bool isSensorFailure(SensorStatus status) {
    return status == SensorStatus::CommunicationError || status == SensorStatus::Failed;
}

}  // namespace

CaptureResult captureFinger(IFingerprintSensor& sensor, IClock& clock, IFeedback& feedback, uint8_t buffer,
                            FeedbackSignal prompt, const CaptureSettings& settings) {
    feedback.signal(prompt);
    const uint64_t deadline = clock.monotonicMs() + settings.fingerTimeoutMs;
    uint8_t lowQuality = 0;
    uint8_t failures = 0;

    while (clock.monotonicMs() < deadline) {
        SensorStatus status = sensor.captureImage();
        if (status == SensorStatus::NoFinger) {
            clock.sleepMs(settings.pollIntervalMs);
            continue;
        }
        if (status == SensorStatus::Ok) {
            status = sensor.imageToTemplate(buffer);
            if (status == SensorStatus::Ok) {
                return CaptureResult::Captured;
            }
        }
        if (isSensorFailure(status)) {
            if (++failures >= settings.maxCommunicationErrors) {
                return CaptureResult::SensorError;
            }
            clock.sleepMs(settings.pollIntervalMs);
            continue;
        }

        // A poor image or character file: lift the finger and try again, a bounded number of times.
        failures = 0;
        if (++lowQuality >= settings.maxLowQualityAttempts) {
            return CaptureResult::LowQuality;
        }
        feedback.signal(FeedbackSignal::LowQuality);
        if (waitForFingerRemoved(sensor, clock, settings) == RemovalResult::SensorError) {
            return CaptureResult::SensorError;
        }
        feedback.signal(prompt);
    }
    return CaptureResult::Timeout;
}

RemovalResult waitForFingerRemoved(IFingerprintSensor& sensor, IClock& clock, const CaptureSettings& settings) {
    const uint64_t deadline = clock.monotonicMs() + settings.removeTimeoutMs;
    uint8_t failures = 0;
    while (clock.monotonicMs() < deadline) {
        const SensorStatus status = sensor.captureImage();
        if (status == SensorStatus::NoFinger) {
            return RemovalResult::Removed;
        }
        if (isSensorFailure(status)) {
            if (++failures >= settings.maxCommunicationErrors) {
                return RemovalResult::SensorError;
            }
        } else {
            failures = 0;
        }
        clock.sleepMs(settings.pollIntervalMs);
    }
    return RemovalResult::Timeout;
}

}  // namespace sciph
