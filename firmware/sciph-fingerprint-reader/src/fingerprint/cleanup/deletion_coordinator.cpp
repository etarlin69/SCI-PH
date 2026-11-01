#include "fingerprint/cleanup/deletion_coordinator.h"

namespace sciph {

namespace {

constexpr const char* kOperation = "deletion";

}  // namespace

DeletionCoordinator::DeletionCoordinator(DeviceApiClient& api, IFingerprintSensor& sensor, SlotMap& slots,
                                         DeletionAttemptStore& store, IRandomSource& random, IClock& clock,
                                         ILogger& logger, const ReplaySettings& replay, uint32_t sensorErrorRetryMs)
    : api_(api),
      sensor_(sensor),
      slots_(slots),
      store_(store),
      random_(random),
      clock_(clock),
      logger_(logger),
      backoff_(replay.initialDelayMs, replay.maxDelayMs),
      sensorErrorRetryMs_(sensorErrorRetryMs) {}

OperationReport DeletionCoordinator::recover() {
    OperationReport report;
    DeletionAttempt stored;
    if (!store_.load(stored)) {
        return report;
    }
    attempt_ = stored;
    pending_ = true;
    logger_.write(LogLevel::Info, kOperation,
                  "deletion " + shortId(attempt_.deletionId) + " attempt " + shortId(attempt_.attemptId) +
                      " found, resending " + outcomeName(attempt_.outcome));
    submit(report);
    return report;
}

OperationReport DeletionCoordinator::poll(const JournalGuard& journal) {
    OperationReport report;
    if (pending_ || clock_.monotonicMs() < nextPhysicalAttemptMs_) {
        return report;
    }
    const ApiResult<PendingDeletion> pending = api_.pendingDeletion();
    report.record(pending.info);
    if (pending.info.status == ApiStatus::InvalidResponse) {
        logger_.write(LogLevel::Warning, kOperation, "pending deletion: invalid_response");
    }
    if (!pending.found) {
        return report;
    }

    const PendingDeletion& job = pending.value;
    // An enrollment in flight owns its slot until the server decides; an unreadable journal owns an unknown one.
    if (journal.unreadable || (journal.hasSlot && journal.slot == job.slot)) {
        logger_.write(LogLevel::Warning, kOperation,
                      "deletion " + shortId(job.deletionId) + " deferred (enrollment journal pending)");
        return report;
    }

    const DeletionOutcome outcome = deleteSlot(job.slot, report);
    attempt_.deletionId = job.deletionId;
    attempt_.attemptId = newUuidV4(random_);
    attempt_.outcome = outcome;
    pending_ = true;
    backoff_.reset();
    if (!store_.save(attempt_)) {
        logger_.write(LogLevel::Warning, kOperation,
                      "deletion " + shortId(attempt_.deletionId) + " attempt not persisted (storage_error)");
    }
    logger_.write(LogLevel::Info, kOperation,
                  "deletion " + shortId(attempt_.deletionId) + " attempt " + shortId(attempt_.attemptId) + ": " +
                      outcomeName(outcome));
    submit(report);
    return report;
}

OperationReport DeletionCoordinator::replay() {
    OperationReport report;
    if (pending_) {
        submit(report);
    }
    return report;
}

DeletionOutcome DeletionCoordinator::deleteSlot(int slot, OperationReport& report) {
    debugSlot(logger_, kOperation, "deleting slot", slot);
    // Outside this sensor's library the slot cannot hold a template.
    if (!slots_.inRange(slot)) {
        return DeletionOutcome::NotFound;
    }
    const SensorStatus probe = sensor_.probeSlot(slot);
    if (probe == SensorStatus::Empty || probe == SensorStatus::OutOfRange) {
        slots_.set(slot, SlotState::Empty);
        return DeletionOutcome::NotFound;
    }
    if (probe == SensorStatus::Occupied && sensor_.deleteModel(slot) == SensorStatus::Ok &&
        sensor_.probeSlot(slot) == SensorStatus::Empty) {
        slots_.set(slot, SlotState::Empty);
        return DeletionOutcome::Deleted;
    }
    slots_.set(slot, SlotState::Unknown);
    report.sensorFault = true;
    return DeletionOutcome::SensorError;
}

void DeletionCoordinator::submit(OperationReport& report) {
    const ApiResponseInfo info = api_.submitDeletion(attempt_.deletionId, attempt_.attemptId, attempt_.outcome);
    report.record(info);
    const std::string prefix = "deletion " + shortId(attempt_.deletionId) + " result " + outcomeName(attempt_.outcome) + " ";
    if (info.status == ApiStatus::NoContent || isDefinitiveRejection(info.status)) {
        logger_.write(info.status == ApiStatus::NoContent ? LogLevel::Info : LogLevel::Warning, kOperation,
                      prefix + (info.status == ApiStatus::NoContent ? "accepted" : "rejected") + " (" +
                          apiStatusName(info.status) + ")");
        if (info.status == ApiStatus::NoContent && attempt_.outcome == DeletionOutcome::SensorError) {
            // The job stays pending on the server; the next physical attempt (new attemptId) waits.
            nextPhysicalAttemptMs_ = clock_.monotonicMs() + sensorErrorRetryMs_;
        }
        store_.clear();
        pending_ = false;
        attempt_ = DeletionAttempt();
        backoff_.reset();
        return;
    }
    // Uncertain, 401 or 403: the same attemptId and outcome are sent again later.
    logger_.write(LogLevel::Warning, kOperation, prefix + "uncertain (" + apiStatusName(info.status) + "), will resend");
    nextAttemptMs_ = clock_.monotonicMs() + backoff_.nextDelayMs();
}

}  // namespace sciph
