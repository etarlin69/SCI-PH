#pragma once

#include <string>

#include "api/device_api_client.h"
#include "config/reader_settings.h"
#include "core/logger.h"
#include "feedback/feedback.h"
#include "fingerprint/capture/finger_capture.h"
#include "fingerprint/r307/fingerprint_sensor.h"
#include "network/backoff.h"
#include "state/operation_report.h"
#include "storage/journal/enrollment_journal.h"
#include "storage/slot_map/slot_map.h"

namespace sciph {

// What reconciliation and deletion must leave alone because of the enrollment in flight.
struct JournalGuard {
    // The journal exists but cannot be read: no slot is known, so no orphan may be deleted.
    bool unreadable = false;
    bool hasSlot = false;
    int slot = -1;
};

// Fingerprint enrollment (ADR-010): the reader chooses an empty slot, journals it, captures twice, creates the model,
// probes the slot again, stores, and reports Enrolled + slot. The template keeps its slot only after a 204.
class EnrollmentCoordinator {
public:
    EnrollmentCoordinator(DeviceApiClient& api, IFingerprintSensor& sensor, SlotMap& slots,
                          const ServerSlotSnapshot& serverSlots, EnrollmentJournalStore& journalStore,
                          IFeedback& feedback, IClock& clock, ILogger& logger, const CaptureSettings& capture,
                          const ReplaySettings& replay);

    // Reads the journal from NVS without acting on it, so that the boot reconciliation (which runs first) already
    // protects its slot.
    void loadJournal();
    // Boot: Storing + empty slot -> clear; Storing + occupied -> Stored; Stored -> replay the same result;
    // Compensating -> delete the slot, then clear. Offline -> keep everything for later.
    OperationReport recover();
    OperationReport poll();
    OperationReport replay();

    // A journal or an unsent failure result: no new enrollment until it is resolved.
    bool busy() const { return journalLoad_ != JournalLoad::None || failurePending_; }
    bool replayDue(uint64_t now) const;
    JournalGuard journalGuard() const;
    bool journalUnreadable() const { return journalLoad_ == JournalLoad::Unreadable; }
    const char* journalStatus() const;
    // Serial command "journal discard": only an unreadable journal can be discarded by hand.
    bool discardUnreadableJournal();

private:
    enum class SlotChoice { Found, NoFreeSlot, SensorError };

    SlotChoice chooseSlot(int& slot, int excluded);
    void resolveJournal(OperationReport& report);
    void submitStored(OperationReport& report);
    void compensate(OperationReport& report);
    // A capture that ended before any Store: the journal is cleared and the failure (if any) reported.
    void handleCaptureFailure(OperationReport& report, const std::string& enrollmentId, CaptureResult result);
    void failEnrollment(OperationReport& report, const std::string& enrollmentId, EnrollmentOutcome outcome,
                        bool sensorFault);
    void submitFailure(OperationReport& report);
    bool saveJournal();
    void clearJournal();
    void scheduleRetry();

    DeviceApiClient& api_;
    IFingerprintSensor& sensor_;
    SlotMap& slots_;
    const ServerSlotSnapshot& serverSlots_;
    EnrollmentJournalStore& journalStore_;
    IFeedback& feedback_;
    IClock& clock_;
    ILogger& logger_;
    CaptureSettings capture_;
    ReplaySettings replaySettings_;
    ExponentialBackoff backoff_;

    JournalLoad journalLoad_ = JournalLoad::None;
    EnrollmentJournal journal_;
    uint64_t nextAttemptMs_ = 0;

    // A Failed / LowQuality / SensorError result (no slot depends on it), kept in RAM only.
    bool failurePending_ = false;
    std::string failureEnrollmentId_;
    EnrollmentOutcome failureOutcome_ = EnrollmentOutcome::SensorError;
    uint64_t failureFirstAttemptMs_ = 0;
};

}  // namespace sciph
