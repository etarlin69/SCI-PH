#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace sciph {

enum class SlotState : uint8_t { Unknown = 0, Empty = 1, Occupied = 2 };

// Physical occupancy of the sensor, in RAM only. Unknown slots (never read, or the sensor gave an answer that is not
// clearly empty or occupied) are never chosen for a Store and never deleted. Even an Empty entry is only a hint: every
// Store probes the slot again first.
class SlotMap {
public:
    void reset(uint16_t capacity);
    uint16_t capacity() const { return static_cast<uint16_t>(states_.size()); }
    bool inRange(int slot) const { return slot >= 0 && static_cast<size_t>(slot) < states_.size(); }
    SlotState get(int slot) const;
    void set(int slot, SlotState state);
    size_t count(SlotState state) const;
    std::vector<int> slotsIn(SlotState state) const;
    bool scanned() const { return scanned_; }
    void markScanned() { scanned_ = true; }

private:
    std::vector<uint8_t> states_;
    bool scanned_ = false;
};

// The latest GET /api/device/fingerprint/slots: what SCI-PH considers occupied (active references plus pending
// deletions). A snapshot, not a reservation.
class ServerSlotSnapshot {
public:
    void update(const std::vector<int>& slots);
    bool known() const { return known_; }
    bool contains(int slot) const;
    size_t size() const { return slots_.size(); }

private:
    std::vector<int> slots_;
    bool known_ = false;
};

}  // namespace sciph
