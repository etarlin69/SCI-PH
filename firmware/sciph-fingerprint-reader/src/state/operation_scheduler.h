#pragma once

#include <cstdint>

#include "config/reader_settings.h"

namespace sciph {

enum class Operation {
    None,
    IdentificationReplay,
    IdentificationPoll,
    EnrollmentReplay,
    EnrollmentPoll,
    DeletionReplay,
    DeletionPoll,
    Reconciliation,
    Heartbeat
};

const char* operationName(Operation operation);

// The scope each endpoint needs: a 403 suspends the whole family (a configuration problem, not retried busily).
enum class ScopeFamily { Identify, Enroll, Heartbeat };

ScopeFamily scopeFamilyOf(Operation operation);

// What the coordinators currently hold.
struct PendingWork {
    bool identificationReplayDue = false;
    bool identificationBusy = false;
    bool enrollmentReplayDue = false;
    bool enrollmentBusy = false;
    bool deletionReplayDue = false;
    bool deletionBusy = false;
};

// One physical operation at a time, by priority: identification (emergencies) > enrollment > deletion >
// reconciliation > heartbeat. Honors 429 Retry-After for every endpoint and 403 per scope family.
class OperationScheduler {
public:
    explicit OperationScheduler(const SchedulerSettings& settings) : settings_(settings) {}

    // Normal polling starts here (after boot reconciliation and journal recovery).
    void start(uint64_t now);
    Operation next(uint64_t now, const PendingWork& work) const;
    // A poll, reconciliation or heartbeat ran: its next run is one interval after `now`.
    void markRan(Operation operation, uint64_t now);
    void reconciliationRan(uint64_t now, bool completed);
    void requestReconciliation(uint64_t now) { nextReconciliationMs_ = now; }

    void throttle(uint64_t now, int retryAfterSeconds);
    bool throttled(uint64_t now) const { return now < throttledUntilMs_; }
    uint64_t throttledUntil() const { return throttledUntilMs_; }

    void suspend(ScopeFamily family, uint64_t now);
    bool suspended(ScopeFamily family, uint64_t now) const;
    void resumeAll();

private:
    SchedulerSettings settings_;
    bool started_ = false;
    uint64_t nextIdentificationMs_ = 0;
    uint64_t nextEnrollmentMs_ = 0;
    uint64_t nextDeletionMs_ = 0;
    uint64_t nextHeartbeatMs_ = 0;
    uint64_t nextReconciliationMs_ = UINT64_MAX;
    uint64_t throttledUntilMs_ = 0;
    uint64_t suspendedUntilMs_[3] = {0, 0, 0};
};

}  // namespace sciph
