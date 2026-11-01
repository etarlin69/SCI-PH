#pragma once

#include <cstdint>
#include <string>
#include <vector>

#include "api/device_api_client.h"
#include "config/reader_settings.h"
#include "core/logger.h"
#include "feedback/feedback.h"
#include "fingerprint/cleanup/deletion_coordinator.h"
#include "fingerprint/enrollment/enrollment_coordinator.h"
#include "fingerprint/identification/identification_coordinator.h"
#include "fingerprint/r307/fingerprint_sensor.h"
#include "fingerprint/reconciliation/physical_slot_scanner.h"
#include "fingerprint/reconciliation/slot_reconciler.h"
#include "network/backoff.h"
#include "network/network_link.h"
#include "state/operation_scheduler.h"
#include "state/reader_state.h"
#include "storage/slot_map/slot_map.h"

namespace sciph {

struct ReaderDependencies {
    IClock& clock;
    ILogger& logger;
    IFeedback& feedback;
    INetworkLink& network;
    IFingerprintSensor& sensor;
    DeviceApiClient& api;
    SlotMap& slots;
    const ServerSlotSnapshot& serverSlots;
    PhysicalSlotScanner& scanner;
    SlotReconciler& reconciler;
    EnrollmentCoordinator& enrollment;
    IdentificationCoordinator& identification;
    DeletionCoordinator& deletion;
    OperationScheduler& scheduler;
};

// The reader's state machine:
// BOOT -> PROVISIONING (no configuration) | WIFI_CONNECT -> TIME_SYNC -> SENSOR_INIT -> READ_PARAMETERS ->
// SLOT_RECONCILIATION -> JOURNAL_RECOVERY -> IDLE, with NETWORK_RECOVERY, AUTH_FAILED and SENSOR_ERROR.
// Non-blocking except for the physical operations themselves; call tick() from loop().
class ReaderController {
public:
    ReaderController(const ReaderDependencies& dependencies, const ControllerSettings& settings,
                     const char* firmwareVersion);

    // requireTime: HTTPS needs a valid clock to validate certificates; development HTTP may continue without it.
    void begin(bool provisioned, bool requireTime);
    void tick();

    ReaderState state() const { return state_; }
    void requestReconciliation(bool allowLargeDeletion);
    std::vector<std::string> statusLines() const;

private:
    void enter(ReaderState state);
    void runOperation(Operation operation);
    // 401 / 403 / 429 / no response, and a sensor health check after a sensor fault.
    void handle(const OperationReport& report, ScopeFamily family);
    void enterAuthFailed();
    void onSensorFault();
    void handleReconciliation(const ReconciliationReport& report, uint64_t now);
    uint32_t uptimeSeconds() const;

    ReaderDependencies deps_;
    ControllerSettings settings_;
    const char* firmwareVersion_;
    ExponentialBackoff wifiBackoff_;

    ReaderState state_ = ReaderState::Boot;
    uint64_t enteredAtMs_ = 0;
    bool provisioned_ = false;
    bool requireTime_ = true;
    bool bootCompleted_ = false;
    bool timeWarningLogged_ = false;
    bool allowLargeDeletion_ = false;
    uint16_t capacity_ = 0;
    uint64_t nextWifiAttemptMs_ = 0;
    uint64_t nextAuthProbeMs_ = 0;
    uint64_t nextSensorRetryMs_ = 0;
    ReconciliationResult lastReconciliation_ = ReconciliationResult::NotScanned;
};

}  // namespace sciph
