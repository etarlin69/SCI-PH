#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace sciph {

// "v|field|field": the compact single-value records kept in NVS. Fields never contain '|' (ids are UUIDs).
std::vector<std::string> splitRecord(const std::string& text, char separator);
bool parseInteger(const std::string& text, int64_t& value);

}  // namespace sciph
