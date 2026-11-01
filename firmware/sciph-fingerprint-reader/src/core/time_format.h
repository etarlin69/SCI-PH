#pragma once

#include <cstdint>
#include <string>

namespace sciph {

// ISO-8601 date-time with an offset ("2026-10-27T12:01:30.1234567+00:00" or "...Z") to Unix seconds. Used only as a
// local hint (expiresAtUtc): the server stays authoritative on whether a session or enrollment is still open.
bool parseIso8601(const std::string& text, int64_t& epochSeconds);

}  // namespace sciph
