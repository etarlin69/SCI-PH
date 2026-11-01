#pragma once

#include "api/device_api_client.h"
#include "config/reader_settings.h"
#include "core/logger.h"
#include "core/uuid.h"
#include "fingerprint/cleanup/deletion_attempt.h"
#include "fingerprint/enrollment/enrollment_coordinator.h"
#include "fingerprint/r307/fingerprint_sensor.h"
#include "network/backoff.h"
#include "state/operation_report.h"
#include "storage/slot_map/slot_map.h"

namespace sciph {

// Physical cleanup of a revoked fingerprint (ADR-011): one attemptId per physical attempt, persisted and resent
// unchanged while the answer is uncertain. After an accepted SensorError the next physical attempt gets a new one.
class DeletionCoordinator {
public:
    DeletionCoordinator(DeviceApiClient& api, IFingerprintSensor& sensor, SlotMap& slots, DeletionAttemptStore& store,
                        IRandomSource& random, IClock& clock, ILogger& logger, const ReplaySettings& replay,
                        uint32_t sensorErrorRetryMs);

    // Boot: a persisted attempt is resent before any new one.
    OperationReport recover();
    OperationReport poll(const JournalGuard& journal);
    OperationReport replay();

    bool busy() const { return pending_; }
    bool replayDue(uint64_t now) const { return pending_ && now >= nextAttemptMs_; }

private:
    DeletionOutcome deleteSlot(int slot, OperationReport& report);
    void submit(OperationReport& report);

    DeviceApiClient& api_;
    IFingerprintSensor& sensor_;
    SlotMap& slots_;
    DeletionAttemptStore& store_;
    IRandomSource& random_;
    IClock& clock_;
    ILogger& logger_;
    ExponentialBackoff backoff_;
    uint32_t sensorErrorRetryMs_;

    bool pending_ = false;
    DeletionAttempt attempt_;
    uint64_t nextAttemptMs_ = 0;
    uint64_t nextPhysicalAttemptMs_ = 0;
};

}  // namespace sciph
