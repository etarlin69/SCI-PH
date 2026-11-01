#pragma once

#include <cstddef>

#include "fingerprint/r307/fingerprint_sensor.h"
#include "storage/slot_map/slot_map.h"

namespace sciph {

struct ScanReport {
    bool completed = false;
    bool usedIndexTable = false;
    size_t occupied = 0;
    size_t unknown = 0;
};

// Reads which slots physically hold a template. Preferred: ReadIndexTable (0x1F), accepted only when it agrees with
// the template count and a sample of LoadChar probes. Otherwise LoadChar on every slot up to the sensor capacity
// (slow on a 1000-slot sensor; normal polling does not start until it ends).
class PhysicalSlotScanner {
public:
    explicit PhysicalSlotScanner(IFingerprintSensor& sensor) : sensor_(sensor) {}
    ScanReport scan(SlotMap& slots);

private:
    bool scanWithIndexTable(SlotMap& slots);

    IFingerprintSensor& sensor_;
};

}  // namespace sciph
