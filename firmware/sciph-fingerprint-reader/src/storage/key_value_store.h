#pragma once

#include <string>

namespace sciph {

// Persistent key/value storage (NVS on the ESP32, memory in native tests). One put is one atomic NVS write: records
// that must change together (journal, deletion attempt) are serialized into a single value.
class IKeyValueStore {
public:
    virtual ~IKeyValueStore() = default;
    virtual bool get(const char* space, const char* key, std::string& value) = 0;
    virtual bool put(const char* space, const char* key, const std::string& value) = 0;
    virtual bool remove(const char* space, const char* key) = 0;
};

}  // namespace sciph
