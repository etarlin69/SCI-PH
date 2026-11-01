#pragma once

#if defined(ARDUINO)

#include "storage/key_value_store.h"

namespace sciph {

// NVS through the Arduino Preferences library. Plain NVS for the academic prototype; production should enable flash
// encryption + encrypted NVS (README, "Secretos").
class NvsKeyValueStore : public IKeyValueStore {
public:
    bool get(const char* space, const char* key, std::string& value) override;
    bool put(const char* space, const char* key, const std::string& value) override;
    bool remove(const char* space, const char* key) override;
};

}  // namespace sciph

#endif
