#include "state/operation_scheduler.h"

namespace sciph {

const char* operationName(Operation operation) {
    switch (operation) {
        case Operation::None:
            return "none";
        case Operation::IdentificationReplay:
            return "identification-replay";
        case Operation::IdentificationPoll:
            return "identification-poll";
        case Operation::EnrollmentReplay:
            return "enrollment-replay";
        case Operation::EnrollmentPoll:
            return "enrollment-poll";
        case Operation::DeletionReplay:
            return "deletion-replay";
        case Operation::DeletionPoll:
            return "deletion-poll";
        case Operation::Reconciliation:
            return "reconciliation";
        case Operation::Heartbeat:
            return "heartbeat";
    }
    return "none";
}

ScopeFamily scopeFamilyOf(Operation operation) {
    switch (operation) {
        case Operation::IdentificationReplay:
        case Operation::IdentificationPoll:
            return ScopeFamily::Identify;
        case Operation::Heartbeat:
        case Operation::None:
            return ScopeFamily::Heartbeat;
        default:
            return ScopeFamily::Enroll;
    }
}

void OperationScheduler::start(uint64_t now) {
    if (started_) {
        return;
    }
    started_ = true;
    nextIdentificationMs_ = now;
    nextEnrollmentMs_ = now;
    nextDeletionMs_ = now;
    nextHeartbeatMs_ = now;
}

Operation OperationScheduler::next(uint64_t now, const PendingWork& work) const {
    if (!started_ || throttled(now)) {
        return Operation::None;
    }
    if (!suspended(ScopeFamily::Identify, now)) {
        if (work.identificationReplayDue) {
            return Operation::IdentificationReplay;
        }
        if (!work.identificationBusy && now >= nextIdentificationMs_) {
            return Operation::IdentificationPoll;
        }
    }
    if (!suspended(ScopeFamily::Enroll, now)) {
        if (work.enrollmentReplayDue) {
            return Operation::EnrollmentReplay;
        }
        if (!work.enrollmentBusy && now >= nextEnrollmentMs_) {
            return Operation::EnrollmentPoll;
        }
        if (work.deletionReplayDue) {
            return Operation::DeletionReplay;
        }
        if (!work.deletionBusy && now >= nextDeletionMs_) {
            return Operation::DeletionPoll;
        }
        if (now >= nextReconciliationMs_) {
            return Operation::Reconciliation;
        }
    }
    if (!suspended(ScopeFamily::Heartbeat, now) && now >= nextHeartbeatMs_) {
        return Operation::Heartbeat;
    }
    return Operation::None;
}

void OperationScheduler::markRan(Operation operation, uint64_t now) {
    switch (operation) {
        case Operation::IdentificationPoll:
            nextIdentificationMs_ = now + settings_.identificationPollMs;
            break;
        case Operation::EnrollmentPoll:
            nextEnrollmentMs_ = now + settings_.enrollmentPollMs;
            break;
        case Operation::DeletionPoll:
            nextDeletionMs_ = now + settings_.deletionPollMs;
            break;
        case Operation::Heartbeat:
            nextHeartbeatMs_ = now + settings_.heartbeatMs;
            break;
        default:
            break;
    }
}

void OperationScheduler::reconciliationRan(uint64_t now, bool completed) {
    if (!completed) {
        nextReconciliationMs_ = now + settings_.reconciliationRetryMs;
    } else if (settings_.reconciliationIntervalMs == 0) {
        nextReconciliationMs_ = UINT64_MAX;
    } else {
        nextReconciliationMs_ = now + settings_.reconciliationIntervalMs;
    }
}

void OperationScheduler::throttle(uint64_t now, int retryAfterSeconds) {
    uint64_t delay = settings_.defaultRetryAfterMs;
    if (retryAfterSeconds >= 0) {
        delay = static_cast<uint64_t>(retryAfterSeconds) * 1000ULL;
        if (delay == 0) {
            delay = 1000;
        }
    }
    if (delay > settings_.maxRetryAfterMs) {
        delay = settings_.maxRetryAfterMs;
    }
    if (now + delay > throttledUntilMs_) {
        throttledUntilMs_ = now + delay;
    }
}

void OperationScheduler::suspend(ScopeFamily family, uint64_t now) {
    suspendedUntilMs_[static_cast<int>(family)] = now + settings_.forbiddenRetryMs;
}

bool OperationScheduler::suspended(ScopeFamily family, uint64_t now) const {
    return now < suspendedUntilMs_[static_cast<int>(family)];
}

void OperationScheduler::resumeAll() {
    throttledUntilMs_ = 0;
    for (uint64_t& until : suspendedUntilMs_) {
        until = 0;
    }
}

}  // namespace sciph
