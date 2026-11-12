#include "api/http_transport.h"

namespace sciph {

std::vector<HttpHeader> deviceAuthHeaders(const DeviceConfig& config) {
    return {{"X-Device-Id", config.deviceCode}, {"X-Device-Key", config.deviceKey}};
}

std::string buildRequestUrl(const std::string& baseUrl, const std::string& path) {
    const std::string base = normalizeBaseUrl(baseUrl);
    if (path.empty() || path[0] != '/') {
        return base + "/" + path;
    }
    return base + path;
}

int parseRetryAfter(const std::string& value) {
    size_t start = 0;
    size_t end = value.size();
    while (start < end && value[start] == ' ') {
        ++start;
    }
    while (end > start && value[end - 1] == ' ') {
        --end;
    }
    if (start == end || end - start > 6) {
        return -1;
    }
    int seconds = 0;
    for (size_t i = start; i < end; ++i) {
        if (value[i] < '0' || value[i] > '9') {
            return -1;
        }
        seconds = seconds * 10 + (value[i] - '0');
    }
    return seconds;
}

}  // namespace sciph
