#include "fingerprint/identification/identification_coordinator.h"

#include "fingerprint/capture/finger_capture.h"

namespace sciph {

namespace {

constexpr const char* kOperation = "identification";

}  // namespace

IdentificationCoordinator::IdentificationCoordinator(DeviceApiClient& api, IFingerprintSensor& sensor,
                                                     IFeedback& feedback, IClock& clock, ILogger& logger,
                                                     const CaptureSettings& capture, const ReplaySettings& replay)
    : api_(api),
      sensor_(sensor),
      feedback_(feedback),
      clock_(clock),
      logger_(logger),
      capture_(capture),
      replaySettings_(replay),
      backoff_(replay.initialDelayMs, replay.maxDelayMs) {}

OperationReport IdentificationCoordinator::poll() {
    OperationReport report;
    const ApiResult<PendingWorkItem> pending = api_.pendingSession();
    report.record(pending.info);
    if (pending.info.status == ApiStatus::InvalidResponse) {
        logger_.write(LogLevel::Warning, kOperation, "pending session: invalid_response");
    }
    if (!pending.found) {
        return report;
    }

    const std::string& sessionId = pending.value.id;
    logger_.write(LogLevel::Info, kOperation, "session " + shortId(sessionId) + " pending");

    const CaptureResult capture =
        captureFinger(sensor_, clock_, feedback_, 1, FeedbackSignal::PlaceFinger, capture_);
    IdentificationOutcome outcome = IdentificationOutcome::SensorError;
    int slot = -1;
    switch (capture) {
        case CaptureResult::Timeout:
            // No result: the session stays Pending on the server and the next poll prompts again until it expires.
            logger_.write(LogLevel::Info, kOperation, "session " + shortId(sessionId) + " capture timeout");
            feedback_.signal(FeedbackSignal::Ready);
            return report;
        case CaptureResult::LowQuality:
            outcome = IdentificationOutcome::LowQuality;
            break;
        case CaptureResult::SensorError:
            outcome = IdentificationOutcome::SensorError;
            report.sensorFault = true;
            break;
        case CaptureResult::Captured: {
            const SearchResult search = sensor_.search();
            if (search.status == SensorStatus::Ok && search.slot >= 0) {
                outcome = IdentificationOutcome::Match;
                slot = search.slot;
            } else if (search.status == SensorStatus::NoMatch) {
                outcome = IdentificationOutcome::NoMatch;
            } else {
                outcome = IdentificationOutcome::SensorError;
                report.sensorFault = true;
            }
            break;
        }
    }

    switch (outcome) {
        case IdentificationOutcome::Match:
            feedback_.signal(FeedbackSignal::Success);
            debugSlot(logger_, kOperation, "matched slot", slot);
            break;
        case IdentificationOutcome::NoMatch:
            feedback_.signal(FeedbackSignal::NotRecognized);
            break;
        case IdentificationOutcome::LowQuality:
            feedback_.signal(FeedbackSignal::LowQuality);
            break;
        case IdentificationOutcome::SensorError:
            feedback_.signal(FeedbackSignal::SensorError);
            break;
    }

    pending_ = true;
    sessionId_ = sessionId;
    outcome_ = outcome;
    slot_ = slot;
    firstAttemptMs_ = clock_.monotonicMs();
    backoff_.reset();
    submit(report);
    return report;
}

OperationReport IdentificationCoordinator::replay() {
    OperationReport report;
    if (!pending_) {
        return report;
    }
    if (clock_.monotonicMs() - firstAttemptMs_ > replaySettings_.abandonAfterMs) {
        logger_.write(LogLevel::Warning, kOperation, "session " + shortId(sessionId_) + " result abandoned (expired)");
        pending_ = false;
        return report;
    }
    submit(report);
    return report;
}

void IdentificationCoordinator::submit(OperationReport& report) {
    const ApiResponseInfo info = api_.submitIdentification(sessionId_, outcome_, slot_);
    report.record(info);
    const std::string prefix = "session " + shortId(sessionId_) + " result " + outcomeName(outcome_) + " ";
    if (info.status == ApiStatus::NoContent) {
        logger_.write(LogLevel::Info, kOperation,
                      prefix + "accepted (204, " + std::to_string(info.latencyMs) + " ms)");
        pending_ = false;
        return;
    }
    if (isDefinitiveRejection(info.status)) {
        // 404: not this reader's session. 409: another result, expired, cancelled. Stop processing it.
        logger_.write(LogLevel::Warning, kOperation, prefix + "rejected (" + apiStatusName(info.status) + ")");
        pending_ = false;
        return;
    }
    // Uncertain, 401 or 403: keep the exact result and send it again later.
    nextAttemptMs_ = clock_.monotonicMs() + backoff_.nextDelayMs();
    logger_.write(LogLevel::Warning, kOperation, prefix + "uncertain (" + apiStatusName(info.status) + "), will resend");
}

}  // namespace sciph
