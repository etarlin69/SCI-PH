#include "core/uuid.h"

namespace sciph {

namespace {

bool isHexDigit(char c) { return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'); }

}  // namespace

std::string newUuidV4(IRandomSource& random) {
    uint8_t bytes[16];
    for (int i = 0; i < 16; i += 4) {
        const uint32_t value = random.next32();
        bytes[i] = static_cast<uint8_t>(value >> 24);
        bytes[i + 1] = static_cast<uint8_t>(value >> 16);
        bytes[i + 2] = static_cast<uint8_t>(value >> 8);
        bytes[i + 3] = static_cast<uint8_t>(value);
    }
    bytes[6] = static_cast<uint8_t>((bytes[6] & 0x0F) | 0x40);
    bytes[8] = static_cast<uint8_t>((bytes[8] & 0x3F) | 0x80);

    static const char hex[] = "0123456789abcdef";
    std::string text;
    text.reserve(36);
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) {
            text.push_back('-');
        }
        text.push_back(hex[bytes[i] >> 4]);
        text.push_back(hex[bytes[i] & 0x0F]);
    }
    return text;
}

bool isUuid(const std::string& value) {
    if (value.size() != 36) {
        return false;
    }
    for (size_t i = 0; i < value.size(); ++i) {
        const bool dash = i == 8 || i == 13 || i == 18 || i == 23;
        if (dash ? value[i] != '-' : !isHexDigit(value[i])) {
            return false;
        }
    }
    return true;
}

}  // namespace sciph
