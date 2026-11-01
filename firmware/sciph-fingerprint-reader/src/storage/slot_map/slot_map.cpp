#include "storage/slot_map/slot_map.h"

#include <algorithm>

namespace sciph {

void SlotMap::reset(uint16_t capacity) {
    states_.assign(capacity, static_cast<uint8_t>(SlotState::Unknown));
    scanned_ = false;
}

SlotState SlotMap::get(int slot) const {
    return inRange(slot) ? static_cast<SlotState>(states_[static_cast<size_t>(slot)]) : SlotState::Unknown;
}

void SlotMap::set(int slot, SlotState state) {
    if (inRange(slot)) {
        states_[static_cast<size_t>(slot)] = static_cast<uint8_t>(state);
    }
}

size_t SlotMap::count(SlotState state) const {
    return static_cast<size_t>(std::count(states_.begin(), states_.end(), static_cast<uint8_t>(state)));
}

std::vector<int> SlotMap::slotsIn(SlotState state) const {
    std::vector<int> slots;
    for (size_t i = 0; i < states_.size(); ++i) {
        if (states_[i] == static_cast<uint8_t>(state)) {
            slots.push_back(static_cast<int>(i));
        }
    }
    return slots;
}

void ServerSlotSnapshot::update(const std::vector<int>& slots) {
    slots_ = slots;
    std::sort(slots_.begin(), slots_.end());
    slots_.erase(std::unique(slots_.begin(), slots_.end()), slots_.end());
    known_ = true;
}

bool ServerSlotSnapshot::contains(int slot) const { return std::binary_search(slots_.begin(), slots_.end(), slot); }

}  // namespace sciph
