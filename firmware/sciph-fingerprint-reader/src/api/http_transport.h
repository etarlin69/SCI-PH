#pragma once

#include <string>
#include <utility>
#include <vector>

#include "config/device_config.h"

namespace sciph {

enum class HttpMethod { Get, Post };

struct HttpRequest {
    HttpMethod method = HttpMethod::Get;
    // Path only ("/api/device/heartbeat"). Never a query string: the key travels in a header.
    std::string path;
    std::string body;
};

struct HttpResponse {
    // 0 = no response (DNS, TLS, connection reset, timeout).
    int status = 0;
    std::string body;
    // Seconds from Retry-After, -1 when absent or unreadable.
    int retryAfterSeconds = -1;
};

// One HTTP exchange with the Device API. The transport adds the device headers; nothing above it sees the key.
class IHttpTransport {
public:
    virtual ~IHttpTransport() = default;
    virtual HttpResponse send(const HttpRequest& request) = 0;
};

using HttpHeader = std::pair<std::string, std::string>;

// X-Device-Id (the device CODE, not the credential GUID) and X-Device-Key.
std::vector<HttpHeader> deviceAuthHeaders(const DeviceConfig& config);

std::string buildRequestUrl(const std::string& baseUrl, const std::string& path);

// Retry-After as delta-seconds; -1 for anything else (HTTP dates are not used by the Device API).
int parseRetryAfter(const std::string& value);

}  // namespace sciph
