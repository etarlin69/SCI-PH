#pragma once

#include <string>

#include "api/device_api_client.h"
#include "config/reader_settings.h"
#include "core/logger.h"
#include "feedback/feedback.h"
#include "fingerprint/r307/fingerprint_sensor.h"
#include "network/backoff.h"
#include "state/operation_report.h"

namespace sciph {

// Emergency identification (ADR-009): poll, one capture, search on the sensor, report Match + slot / NoMatch /
// LowQuality / SensorError. The match does not identify anyone: the paramedic confirms in the app.
class IdentificationCoordinator {
public:
    IdentificationCoordinator(DeviceApiClient& api, IFingerprintSensor& sensor, IFeedback& feedback, IClock& clock,
                              ILogger& logger, const CaptureSettings& capture, const ReplaySettings& replay);

    OperationReport poll();
    // Sends the SAME result (session, outcome, slot) again after an uncertain answer.
    OperationReport replay();
    bool replayPending() const { return pending_; }
    bool replayDue(uint64_t now) const { return pending_ && now >= nextAttemptMs_; }

private:
    void submit(OperationReport& report);

    DeviceApiClient& api_;
    IFingerprintSensor& sensor_;
    IFeedback& feedback_;
    IClock& clock_;
    ILogger& logger_;
    CaptureSettings capture_;
    ReplaySettings replaySettings_;
    ExponentialBackoff backoff_;

    bool pending_ = false;
    std::string sessionId_;
    IdentificationOutcome outcome_ = IdentificationOutcome::SensorError;
    int slot_ = -1;
    uint64_t firstAttemptMs_ = 0;
    uint64_t nextAttemptMs_ = 0;
};

}  // namespace sciph
