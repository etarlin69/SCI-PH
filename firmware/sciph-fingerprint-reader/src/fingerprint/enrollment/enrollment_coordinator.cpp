#include "fingerprint/enrollment/enrollment_coordinator.h"

namespace sciph {

namespace {

constexpr const char* kOperation = "enrollment";
// Slots found occupied right before Store before giving up (each one is replaced by another empty slot).
constexpr int kMaxSlotRetries = 3;
// Unclear probe answers tolerated while looking for an empty slot.
constexpr uint8_t kMaxProbeFailures = 3;

}  // namespace

EnrollmentCoordinator::EnrollmentCoordinator(DeviceApiClient& api, IFingerprintSensor& sensor, SlotMap& slots,
                                             const ServerSlotSnapshot& serverSlots,
                                             EnrollmentJournalStore& journalStore, IFeedback& feedback, IClock& clock,
                                             ILogger& logger, const CaptureSettings& capture,
                                             const ReplaySettings& replay)
    : api_(api),
      sensor_(sensor),
      slots_(slots),
      serverSlots_(serverSlots),
      journalStore_(journalStore),
      feedback_(feedback),
      clock_(clock),
      logger_(logger),
      capture_(capture),
      replaySettings_(replay),
      backoff_(replay.initialDelayMs, replay.maxDelayMs) {}

bool EnrollmentCoordinator::replayDue(uint64_t now) const {
    return (journalLoad_ == JournalLoad::Loaded || failurePending_) && now >= nextAttemptMs_;
}

JournalGuard EnrollmentCoordinator::journalGuard() const {
    JournalGuard guard;
    if (journalLoad_ == JournalLoad::Unreadable) {
        guard.unreadable = true;
    } else if (journalLoad_ == JournalLoad::Loaded) {
        guard.hasSlot = true;
        guard.slot = journal_.slot;
    }
    return guard;
}

const char* EnrollmentCoordinator::journalStatus() const {
    switch (journalLoad_) {
        case JournalLoad::None:
            return "none";
        case JournalLoad::Unreadable:
            return "unreadable";
        case JournalLoad::Loaded:
            return journalStateName(journal_.state);
    }
    return "none";
}

bool EnrollmentCoordinator::discardUnreadableJournal() {
    if (journalLoad_ != JournalLoad::Unreadable) {
        return false;
    }
    journalStore_.clear();
    journalLoad_ = JournalLoad::None;
    logger_.write(LogLevel::Warning, kOperation, "unreadable journal discarded by the operator");
    return true;
}

void EnrollmentCoordinator::loadJournal() {
    EnrollmentJournal loaded;
    journalLoad_ = journalStore_.load(loaded);
    journal_ = journalLoad_ == JournalLoad::Loaded ? loaded : EnrollmentJournal();
}

OperationReport EnrollmentCoordinator::recover() {
    OperationReport report;
    loadJournal();
    if (journalLoad_ == JournalLoad::Unreadable) {
        logger_.write(LogLevel::Error, kOperation,
                      "journal unreadable: enrollment and orphan deletion blocked (serial: journal discard)");
        return report;
    }
    if (journalLoad_ == JournalLoad::None) {
        return report;
    }
    logger_.write(LogLevel::Info, kOperation,
                  "journal " + shortId(journal_.enrollmentId) + " found (" + journalStateName(journal_.state) +
                      "), recovering");
    resolveJournal(report);
    return report;
}

OperationReport EnrollmentCoordinator::replay() {
    OperationReport report;
    if (journalLoad_ == JournalLoad::Loaded) {
        resolveJournal(report);
        return report;
    }
    if (failurePending_) {
        if (clock_.monotonicMs() - failureFirstAttemptMs_ > replaySettings_.abandonAfterMs) {
            logger_.write(LogLevel::Warning, kOperation,
                          "enrollment " + shortId(failureEnrollmentId_) + " result abandoned (expired)");
            failurePending_ = false;
            backoff_.reset();
            return report;
        }
        submitFailure(report);
    }
    return report;
}

OperationReport EnrollmentCoordinator::poll() {
    OperationReport report;
    if (busy()) {
        return report;
    }
    const ApiResult<PendingWorkItem> pending = api_.pendingEnrollment();
    report.record(pending.info);
    if (pending.info.status == ApiStatus::InvalidResponse) {
        logger_.write(LogLevel::Warning, kOperation, "pending enrollment: invalid_response");
    }
    if (!pending.found) {
        return report;
    }

    const std::string enrollmentId = pending.value.id;
    logger_.write(LogLevel::Info, kOperation, "enrollment " + shortId(enrollmentId) + " pending");

    // 1. An empty slot: physically empty and not occupied for SCI-PH.
    int slot = -1;
    const SlotChoice choice = chooseSlot(slot, -1);
    if (choice != SlotChoice::Found) {
        const bool full = choice == SlotChoice::NoFreeSlot;
        logger_.write(LogLevel::Warning, kOperation,
                      "enrollment " + shortId(enrollmentId) + (full ? " no free slot (sensor_full)" : " slot probe failed"));
        feedback_.signal(full ? FeedbackSignal::SensorFull : FeedbackSignal::SensorError);
        failEnrollment(report, enrollmentId, full ? EnrollmentOutcome::Failed : EnrollmentOutcome::SensorError, !full);
        return report;
    }

    // 2. The journal BEFORE anything can reach the sensor's flash.
    journal_ = EnrollmentJournal();
    journal_.enrollmentId = enrollmentId;
    journal_.slot = slot;
    journal_.state = JournalState::Storing;
    journal_.hasExpiry = pending.value.hasExpiry;
    journal_.expiresAtEpoch = pending.value.expiresAtEpoch;
    if (!saveJournal()) {
        logger_.write(LogLevel::Error, kOperation, "enrollment " + shortId(enrollmentId) + " journal write failed (storage_error)");
        feedback_.signal(FeedbackSignal::SensorError);
        failEnrollment(report, enrollmentId, EnrollmentOutcome::SensorError, false);
        return report;
    }

    // 3-5. First capture.
    const CaptureResult first =
        captureFinger(sensor_, clock_, feedback_, 1, FeedbackSignal::PlaceFinger, capture_);
    if (first != CaptureResult::Captured) {
        handleCaptureFailure(report, enrollmentId, first);
        return report;
    }

    // 6-7. Lift the finger.
    feedback_.signal(FeedbackSignal::RemoveFinger);
    const RemovalResult removed = waitForFingerRemoved(sensor_, clock_, capture_);
    if (removed != RemovalResult::Removed) {
        handleCaptureFailure(report, enrollmentId,
                             removed == RemovalResult::Timeout ? CaptureResult::Timeout : CaptureResult::SensorError);
        return report;
    }

    // 8-10. Second capture of the same finger.
    const CaptureResult second =
        captureFinger(sensor_, clock_, feedback_, 2, FeedbackSignal::PlaceAgain, capture_);
    if (second != CaptureResult::Captured) {
        handleCaptureFailure(report, enrollmentId, second);
        return report;
    }

    // 11. One model from both captures.
    const SensorStatus model = sensor_.createModel();
    if (model == SensorStatus::Mismatch) {
        clearJournal();
        feedback_.signal(FeedbackSignal::FingersDoNotMatch);
        failEnrollment(report, enrollmentId, EnrollmentOutcome::Failed, false);
        return report;
    }
    if (model != SensorStatus::Ok) {
        clearJournal();
        feedback_.signal(FeedbackSignal::SensorError);
        failEnrollment(report, enrollmentId, EnrollmentOutcome::SensorError, true);
        return report;
    }

    // 12. The R307 Store overwrites an occupied slot: probe it again right before, never trust the map alone.
    bool ready = false;
    for (int attempt = 0; attempt < kMaxSlotRetries && !ready; ++attempt) {
        const SensorStatus probe = sensor_.probeSlot(journal_.slot);
        if (probe == SensorStatus::Empty) {
            ready = true;
            break;
        }
        if (probe != SensorStatus::Occupied) {
            slots_.set(journal_.slot, SlotState::Unknown);
            break;
        }
        slots_.set(journal_.slot, SlotState::Occupied);
        int next = -1;
        if (chooseSlot(next, journal_.slot) != SlotChoice::Found) {
            break;
        }
        journal_.slot = next;
        if (!saveJournal()) {
            break;
        }
    }
    if (!ready) {
        clearJournal();
        feedback_.signal(FeedbackSignal::SensorError);
        failEnrollment(report, enrollmentId, EnrollmentOutcome::SensorError, true);
        return report;
    }

    // 13. Store.
    debugSlot(logger_, kOperation, "storing in slot", journal_.slot);
    const SensorStatus stored = sensor_.storeModel(journal_.slot);
    if (stored != SensorStatus::Ok) {
        logger_.write(LogLevel::Error, kOperation,
                      "enrollment " + shortId(enrollmentId) + " store failed (" + sensorStatusName(stored) + ")");
        journal_.state = JournalState::Compensating;
        saveJournal();
        compensate(report);
        feedback_.signal(FeedbackSignal::SensorError);
        failEnrollment(report, enrollmentId, EnrollmentOutcome::SensorError, true);
        return report;
    }

    // 14. Stored.
    slots_.set(journal_.slot, SlotState::Occupied);
    journal_.state = JournalState::Stored;
    if (!saveJournal()) {
        // Boot recovery treats an occupied Storing slot as Stored, so the slot is still protected.
        logger_.write(LogLevel::Warning, kOperation, "enrollment " + shortId(enrollmentId) + " journal update failed");
    }
    feedback_.signal(FeedbackSignal::Success);

    // 15. Enrolled + slot.
    submitStored(report);
    return report;
}

EnrollmentCoordinator::SlotChoice EnrollmentCoordinator::chooseSlot(int& slot, int excluded) {
    uint8_t failures = 0;
    const int capacity = static_cast<int>(slots_.capacity());
    for (int candidate = 0; candidate < capacity; ++candidate) {
        if (candidate == excluded || slots_.get(candidate) != SlotState::Empty) {
            continue;
        }
        // Physically empty but known to SCI-PH (an active reference or a pending deletion): the server would answer
        // 409, and the slot belongs to its cleanup.
        if (serverSlots_.known() && serverSlots_.contains(candidate)) {
            continue;
        }
        const SensorStatus probe = sensor_.probeSlot(candidate);
        if (probe == SensorStatus::Empty) {
            slot = candidate;
            return SlotChoice::Found;
        }
        if (probe == SensorStatus::Occupied) {
            slots_.set(candidate, SlotState::Occupied);
            continue;
        }
        slots_.set(candidate, SlotState::Unknown);
        if (++failures >= kMaxProbeFailures) {
            return SlotChoice::SensorError;
        }
    }
    return SlotChoice::NoFreeSlot;
}

void EnrollmentCoordinator::resolveJournal(OperationReport& report) {
    switch (journal_.state) {
        case JournalState::Storing: {
            const SensorStatus probe = sensor_.probeSlot(journal_.slot);
            if (probe == SensorStatus::Empty || probe == SensorStatus::OutOfRange) {
                // The Store never happened: nothing on the sensor depends on this enrollment.
                slots_.set(journal_.slot, SlotState::Empty);
                logger_.write(LogLevel::Info, kOperation,
                              "enrollment " + shortId(journal_.enrollmentId) + " was interrupted before Store");
                clearJournal();
                report.journalResolved = true;
                return;
            }
            if (probe == SensorStatus::Occupied) {
                // The slot was verified empty when it was journaled: the template is ours.
                slots_.set(journal_.slot, SlotState::Occupied);
                journal_.state = JournalState::Stored;
                saveJournal();
                submitStored(report);
                return;
            }
            report.sensorFault = true;
            scheduleRetry();
            return;
        }
        case JournalState::Stored:
            submitStored(report);
            return;
        case JournalState::Compensating:
            compensate(report);
            return;
    }
}

void EnrollmentCoordinator::submitStored(OperationReport& report) {
    const ApiResponseInfo info = api_.submitEnrollment(journal_.enrollmentId, EnrollmentOutcome::Enrolled, journal_.slot);
    report.record(info);
    const std::string prefix = "enrollment " + shortId(journal_.enrollmentId) + " result Enrolled ";

    if (info.status == ApiStatus::NoContent) {
        logger_.write(LogLevel::Info, kOperation, prefix + "accepted (204, " + std::to_string(info.latencyMs) + " ms)");
        clearJournal();
        report.journalResolved = true;
        return;
    }
    // 400 / 404 / 409 (expired, cancelled, another result, slot or patient conflict): never applied. A 403 before
    // any uncertain delivery: no submission was ever processed, so no reference points at this slot either.
    const bool neverApplied = isDefinitiveRejection(info.status) ||
                              (info.status == ApiStatus::Forbidden && !journal_.uncertainDelivery);
    if (neverApplied) {
        logger_.write(LogLevel::Warning, kOperation,
                      prefix + "rejected (" + apiStatusName(info.status) + "), deleting the stored template");
        journal_.state = JournalState::Compensating;
        saveJournal();
        compensate(report);
        return;
    }
    if (isUncertainResultResponse(info.status) && !journal_.uncertainDelivery) {
        journal_.uncertainDelivery = true;
        saveJournal();
    }
    // Uncertain, 401, or 403 after an uncertain delivery: keep the slot and send exactly the same result later.
    logger_.write(LogLevel::Warning, kOperation,
                  prefix + "not confirmed (" + apiStatusName(info.status) + "), slot kept, will resend");
    scheduleRetry();
}

void EnrollmentCoordinator::compensate(OperationReport& report) {
    const int slot = journal_.slot;
    const std::string prefix = "enrollment " + shortId(journal_.enrollmentId) + " compensation ";
    const SensorStatus probe = sensor_.probeSlot(slot);
    if (probe == SensorStatus::Empty || probe == SensorStatus::OutOfRange) {
        slots_.set(slot, SlotState::Empty);
        logger_.write(LogLevel::Info, kOperation, prefix + "done (slot already empty)");
        clearJournal();
        report.journalResolved = true;
        return;
    }
    if (probe == SensorStatus::Occupied && sensor_.deleteModel(slot) == SensorStatus::Ok &&
        sensor_.probeSlot(slot) == SensorStatus::Empty) {
        slots_.set(slot, SlotState::Empty);
        logger_.write(LogLevel::Info, kOperation, prefix + "done (template deleted)");
        clearJournal();
        report.journalResolved = true;
        return;
    }
    // Keep the Compensating journal: the slot is retried later, never forgotten.
    slots_.set(slot, SlotState::Unknown);
    report.sensorFault = true;
    logger_.write(LogLevel::Error, kOperation, prefix + "failed (sensor), will retry");
    scheduleRetry();
}

void EnrollmentCoordinator::handleCaptureFailure(OperationReport& report, const std::string& enrollmentId,
                                                 CaptureResult result) {
    clearJournal();
    switch (result) {
        case CaptureResult::Timeout:
            // No result: the enrollment stays Pending and the next poll prompts again until it expires there.
            logger_.write(LogLevel::Info, kOperation, "enrollment " + shortId(enrollmentId) + " capture timeout");
            feedback_.signal(FeedbackSignal::Ready);
            return;
        case CaptureResult::LowQuality:
            feedback_.signal(FeedbackSignal::LowQuality);
            failEnrollment(report, enrollmentId, EnrollmentOutcome::LowQuality, false);
            return;
        case CaptureResult::SensorError:
        case CaptureResult::Captured:
            feedback_.signal(FeedbackSignal::SensorError);
            failEnrollment(report, enrollmentId, EnrollmentOutcome::SensorError, true);
            return;
    }
}

void EnrollmentCoordinator::failEnrollment(OperationReport& report, const std::string& enrollmentId,
                                           EnrollmentOutcome outcome, bool sensorFault) {
    if (sensorFault) {
        report.sensorFault = true;
    }
    failurePending_ = true;
    failureEnrollmentId_ = enrollmentId;
    failureOutcome_ = outcome;
    failureFirstAttemptMs_ = clock_.monotonicMs();
    submitFailure(report);
}

void EnrollmentCoordinator::submitFailure(OperationReport& report) {
    const ApiResponseInfo info = api_.submitEnrollment(failureEnrollmentId_, failureOutcome_, -1);
    report.record(info);
    const std::string prefix =
        "enrollment " + shortId(failureEnrollmentId_) + " result " + outcomeName(failureOutcome_) + " ";
    if (info.status == ApiStatus::NoContent || isDefinitiveRejection(info.status)) {
        logger_.write(info.status == ApiStatus::NoContent ? LogLevel::Info : LogLevel::Warning, kOperation,
                      prefix + (info.status == ApiStatus::NoContent ? "accepted" : "rejected") + " (" +
                          apiStatusName(info.status) + ")");
        failurePending_ = false;
        if (journalLoad_ == JournalLoad::None) {
            backoff_.reset();
        }
        return;
    }
    logger_.write(LogLevel::Warning, kOperation, prefix + "uncertain (" + apiStatusName(info.status) + "), will resend");
    scheduleRetry();
}

bool EnrollmentCoordinator::saveJournal() {
    if (!journalStore_.save(journal_)) {
        return false;
    }
    journalLoad_ = JournalLoad::Loaded;
    return true;
}

void EnrollmentCoordinator::clearJournal() {
    if (!journalStore_.clear()) {
        logger_.write(LogLevel::Warning, kOperation, "journal clear failed (storage_error)");
    }
    journalLoad_ = JournalLoad::None;
    journal_ = EnrollmentJournal();
    backoff_.reset();
}

void EnrollmentCoordinator::scheduleRetry() { nextAttemptMs_ = clock_.monotonicMs() + backoff_.nextDelayMs(); }

}  // namespace sciph
