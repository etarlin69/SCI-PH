#pragma once

#include <cstdint>
#include <string>

namespace sciph {

class IRandomSource {
public:
    virtual ~IRandomSource() = default;
    virtual uint32_t next32() = 0;
};

// Random (version 4) UUID, lowercase, 36 characters.
std::string newUuidV4(IRandomSource& random);

// 8-4-4-4-12 hexadecimal: the shape of every id the Device API returns. Anything else is never put in a URL.
bool isUuid(const std::string& value);

}  // namespace sciph
