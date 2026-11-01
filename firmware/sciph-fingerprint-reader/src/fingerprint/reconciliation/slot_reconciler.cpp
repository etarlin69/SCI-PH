#include "fingerprint/reconciliation/slot_reconciler.h"

#include <string>

namespace sciph {

namespace {

constexpr const char* kOperation = "reconciliation";

}  // namespace

const char* reconciliationResultName(ReconciliationResult result) {
    switch (result) {
        case ReconciliationResult::Completed:
            return "completed";
        case ReconciliationResult::InventoryUnavailable:
            return "inventory_unavailable";
        case ReconciliationResult::NotScanned:
            return "not_scanned";
        case ReconciliationResult::JournalUnreadable:
            return "journal_unreadable";
        case ReconciliationResult::NeedsConfirmation:
            return "needs_confirmation";
        case ReconciliationResult::SensorError:
            return "sensor_error";
    }
    return "unknown";
}

std::vector<int> orphanCandidates(const SlotMap& slots, const ServerSlotSnapshot& server, const JournalGuard& journal) {
    std::vector<int> candidates;
    if (!server.known()) {
        return candidates;
    }
    for (int slot : slots.slotsIn(SlotState::Occupied)) {
        if (server.contains(slot) || (journal.hasSlot && journal.slot == slot)) {
            continue;
        }
        candidates.push_back(slot);
    }
    return candidates;
}

bool SlotReconciler::fetchInventory(ReconciliationReport& report) {
    const ApiResult<std::vector<int>> inventory = api_.slotInventory();
    report.api = inventory.info;
    if (inventory.info.status != ApiStatus::Ok || !inventory.found) {
        return false;
    }
    std::vector<int> inRange;
    size_t ignored = 0;
    for (int slot : inventory.value) {
        if (slots_.inRange(slot)) {
            inRange.push_back(slot);
        } else {
            ++ignored;
        }
    }
    report.ignoredServerSlots = ignored;
    server_.update(inRange);
    return true;
}

ReconciliationReport SlotReconciler::run(const JournalGuard& journal, bool allowLargeDeletion) {
    ReconciliationReport report;
    if (!slots_.scanned()) {
        report.result = ReconciliationResult::NotScanned;
        return report;
    }

    // (2) A confirmed inventory, or nothing happens.
    if (!fetchInventory(report)) {
        report.result = ReconciliationResult::InventoryUnavailable;
        logger_.write(LogLevel::Warning, kOperation,
                      std::string("inventory unavailable (") + apiStatusName(report.api.status) + "), sensor unchanged");
        return report;
    }

    // (1) + (3)
    std::vector<int> candidates = orphanCandidates(slots_, server_, journal);
    report.candidates = candidates.size();
    if (candidates.empty()) {
        report.result = ReconciliationResult::Completed;
        logger_.write(LogLevel::Info, kOperation,
                      "no orphan slots (" + std::to_string(report.ignoredServerSlots) + " server slots out of range)");
        return report;
    }
    if (journal.unreadable) {
        report.result = ReconciliationResult::JournalUnreadable;
        report.skipped = candidates.size();
        logger_.write(LogLevel::Warning, kOperation,
                      std::to_string(candidates.size()) + " orphan candidates kept: journal unreadable");
        return report;
    }
    if (candidates.size() > settings_.maxAutomaticDeletions && !allowLargeDeletion) {
        report.result = ReconciliationResult::NeedsConfirmation;
        report.skipped = candidates.size();
        logger_.write(LogLevel::Warning, kOperation,
                      std::to_string(candidates.size()) + " orphan candidates exceed the automatic limit: run 'reconcile force'");
        return report;
    }

    // (4) The inventory again, immediately before touching the sensor.
    if (!fetchInventory(report)) {
        report.result = ReconciliationResult::InventoryUnavailable;
        report.skipped = candidates.size();
        logger_.write(LogLevel::Warning, kOperation,
                      std::string("inventory recheck unavailable (") + apiStatusName(report.api.status) +
                          "), sensor unchanged");
        return report;
    }

    bool sensorFailure = false;
    for (int slot : candidates) {
        if (server_.contains(slot) || (journal.hasSlot && journal.slot == slot)) {
            ++report.skipped;
            continue;
        }
        // (5) Still physically there?
        const SensorStatus probe = sensor_.probeSlot(slot);
        if (probe == SensorStatus::Empty) {
            slots_.set(slot, SlotState::Empty);
            continue;
        }
        if (probe != SensorStatus::Occupied) {
            slots_.set(slot, SlotState::Unknown);
            ++report.skipped;
            sensorFailure = true;
            continue;
        }
        debugSlot(logger_, kOperation, "deleting orphan slot", slot);
        if (sensor_.deleteModel(slot) == SensorStatus::Ok && sensor_.probeSlot(slot) == SensorStatus::Empty) {
            slots_.set(slot, SlotState::Empty);
            ++report.deleted;
        } else {
            slots_.set(slot, SlotState::Unknown);
            ++report.skipped;
            sensorFailure = true;
        }
    }

    report.result = sensorFailure ? ReconciliationResult::SensorError : ReconciliationResult::Completed;
    logger_.write(sensorFailure ? LogLevel::Warning : LogLevel::Info, kOperation,
                  std::to_string(report.candidates) + " orphan candidates, " + std::to_string(report.deleted) +
                      " deleted, " + std::to_string(report.skipped) + " kept");
    return report;
}

}  // namespace sciph
