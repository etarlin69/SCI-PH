#include "fingerprint/reconciliation/physical_slot_scanner.h"

#include <vector>

namespace sciph {

namespace {

// Consecutive unclear LoadChar answers before the scan is abandoned (sensor unplugged or failing).
constexpr int kMaxConsecutiveFailures = 5;
// Occupied and empty slots of the index table cross-checked with LoadChar.
constexpr size_t kIndexSampleSize = 4;

}  // namespace

ScanReport PhysicalSlotScanner::scan(SlotMap& slots) {
    ScanReport report;
    if (scanWithIndexTable(slots)) {
        report.usedIndexTable = true;
    } else {
        int consecutiveFailures = 0;
        const int capacity = static_cast<int>(slots.capacity());
        for (int slot = 0; slot < capacity; ++slot) {
            const SensorStatus probe = sensor_.probeSlot(slot);
            if (probe == SensorStatus::Occupied) {
                slots.set(slot, SlotState::Occupied);
                consecutiveFailures = 0;
            } else if (probe == SensorStatus::Empty) {
                slots.set(slot, SlotState::Empty);
                consecutiveFailures = 0;
            } else {
                slots.set(slot, SlotState::Unknown);
                if (++consecutiveFailures >= kMaxConsecutiveFailures) {
                    return report;
                }
            }
        }
    }
    slots.markScanned();
    report.completed = true;
    report.occupied = slots.count(SlotState::Occupied);
    report.unknown = slots.count(SlotState::Unknown);
    return report;
}

bool PhysicalSlotScanner::scanWithIndexTable(SlotMap& slots) {
    const size_t capacity = slots.capacity();
    if (capacity == 0) {
        return false;
    }
    std::vector<bool> occupied(capacity, false);
    size_t occupiedCount = 0;
    const size_t pages = (capacity + 255) / 256;
    for (size_t page = 0; page < pages; ++page) {
        uint8_t bits[32] = {0};
        if (sensor_.readIndexPage(static_cast<uint8_t>(page), bits) != SensorStatus::Ok) {
            return false;
        }
        for (size_t offset = 0; offset < 256; ++offset) {
            const size_t slot = page * 256 + offset;
            if (slot >= capacity) {
                break;
            }
            if ((bits[offset / 8] >> (offset % 8)) & 0x01) {
                occupied[slot] = true;
                ++occupiedCount;
            }
        }
    }

    uint16_t templates = 0;
    if (sensor_.templateCount(templates) != SensorStatus::Ok || templates != occupiedCount) {
        return false;
    }

    // The bit order is not documented for every clone: a few LoadChar probes must agree before the table is used.
    size_t checkedOccupied = 0;
    size_t checkedEmpty = 0;
    for (size_t slot = 0; slot < capacity && (checkedOccupied < kIndexSampleSize || checkedEmpty < kIndexSampleSize);
         ++slot) {
        if (occupied[slot] ? checkedOccupied >= kIndexSampleSize : checkedEmpty >= kIndexSampleSize) {
            continue;
        }
        const SensorStatus probe = sensor_.probeSlot(static_cast<int>(slot));
        if (probe != (occupied[slot] ? SensorStatus::Occupied : SensorStatus::Empty)) {
            return false;
        }
        if (occupied[slot]) {
            ++checkedOccupied;
        } else {
            ++checkedEmpty;
        }
    }

    for (size_t slot = 0; slot < capacity; ++slot) {
        slots.set(static_cast<int>(slot), occupied[slot] ? SlotState::Occupied : SlotState::Empty);
    }
    return true;
}

}  // namespace sciph
