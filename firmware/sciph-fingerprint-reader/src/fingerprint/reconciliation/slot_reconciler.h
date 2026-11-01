#pragma once

#include <cstddef>
#include <vector>

#include "api/device_api_client.h"
#include "config/reader_settings.h"
#include "core/logger.h"
#include "fingerprint/enrollment/enrollment_coordinator.h"
#include "fingerprint/r307/fingerprint_sensor.h"
#include "storage/slot_map/slot_map.h"

namespace sciph {

enum class ReconciliationResult {
    Completed,
    // The inventory could not be confirmed (offline, 401/403/429, 5xx, invalid body): the sensor is left unchanged.
    InventoryUnavailable,
    NotScanned,
    // An unreadable journal may own any slot: nothing is deleted.
    JournalUnreadable,
    // More candidates than ReconciliationSettings::maxAutomaticDeletions: "reconcile force" is required.
    NeedsConfirmation,
    // Some candidates could not be read or deleted; they were left as they are.
    SensorError
};

const char* reconciliationResultName(ReconciliationResult result);

struct ReconciliationReport {
    ReconciliationResult result = ReconciliationResult::InventoryUnavailable;
    ApiResponseInfo api;
    size_t candidates = 0;
    size_t deleted = 0;
    size_t skipped = 0;
    // Server slots beyond this sensor's capacity (counted, never printed).
    size_t ignoredServerSlots = 0;
};

// Physically occupied - server-known (active + pending deletion) - the journal's slot = orphan candidates.
std::vector<int> orphanCandidates(const SlotMap& slots, const ServerSlotSnapshot& server, const JournalGuard& journal);

// Orphan reconciliation (ADR-010 §10). Fails safe: a candidate is deleted only after (1) it is physically occupied,
// (2) absent from a confirmed inventory, (3) not owned by the journal, (4) still absent from a SECOND inventory fetched
// right before deleting, and (5) LoadChar confirms it again.
class SlotReconciler {
public:
    SlotReconciler(DeviceApiClient& api, IFingerprintSensor& sensor, SlotMap& slots, ServerSlotSnapshot& server,
                   ILogger& logger, const ReconciliationSettings& settings)
        : api_(api), sensor_(sensor), slots_(slots), server_(server), logger_(logger), settings_(settings) {}

    ReconciliationReport run(const JournalGuard& journal, bool allowLargeDeletion);

private:
    bool fetchInventory(ReconciliationReport& report);

    DeviceApiClient& api_;
    IFingerprintSensor& sensor_;
    SlotMap& slots_;
    ServerSlotSnapshot& server_;
    ILogger& logger_;
    ReconciliationSettings settings_;
};

}  // namespace sciph
