#if defined(ARDUINO)

#include "storage/nvs_key_value_store_esp32.h"

#include <Preferences.h>

namespace sciph {

bool NvsKeyValueStore::get(const char* space, const char* key, std::string& value) {
    Preferences preferences;
    if (!preferences.begin(space, true)) {
        return false;
    }
    if (!preferences.isKey(key)) {
        preferences.end();
        return false;
    }
    const String stored = preferences.getString(key, "");
    preferences.end();
    value = stored.c_str();
    return true;
}

bool NvsKeyValueStore::put(const char* space, const char* key, const std::string& value) {
    Preferences preferences;
    if (!preferences.begin(space, false)) {
        return false;
    }
    const size_t written = preferences.putString(key, value.c_str());
    preferences.end();
    return written == value.size();
}

bool NvsKeyValueStore::remove(const char* space, const char* key) {
    Preferences preferences;
    if (!preferences.begin(space, false)) {
        return false;
    }
    const bool removed = !preferences.isKey(key) || preferences.remove(key);
    preferences.end();
    return removed;
}

}  // namespace sciph

#endif
