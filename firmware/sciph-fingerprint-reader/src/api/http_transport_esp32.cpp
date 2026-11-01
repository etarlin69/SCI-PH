#if defined(ARDUINO)

#include "api/http_transport_esp32.h"

#include <HTTPClient.h>

#include "firmware_config.h"

namespace sciph {

namespace {

// Largest body read from the Device API (the slot inventory of a 1000-slot sensor is about 5 KB).
constexpr int kMaxResponseBody = 16384;

}  // namespace

HttpResponse Esp32HttpTransport::send(const HttpRequest& request) {
    HttpResponse response;
    const std::string url = buildRequestUrl(config_.apiBaseUrl, request.path);

    HTTPClient http;
    http.setReuse(false);
    http.setConnectTimeout(static_cast<int32_t>(timeoutMs_));
    http.setTimeout(static_cast<uint16_t>(timeoutMs_));

    bool opened = false;
    if (config_.transport == TransportMode::Https) {
        if (!startsWith(url, "https://") || config_.caCertificatePem.empty()) {
            return response;
        }
        if (!caLoaded_) {
            secureClient_.setCACert(config_.caCertificatePem.c_str());
            caLoaded_ = true;
        }
        opened = http.begin(secureClient_, url.c_str());
    } else {
#if SCIPH_ALLOW_DEV_HTTP
        if (!startsWith(url, "http://")) {
            return response;
        }
        opened = http.begin(plainClient_, url.c_str());
#else
        return response;
#endif
    }
    if (!opened) {
        return response;
    }

    const char* collected[] = {"Retry-After"};
    http.collectHeaders(collected, 1);
    for (const HttpHeader& header : deviceAuthHeaders(config_)) {
        http.addHeader(header.first.c_str(), header.second.c_str());
    }
    http.addHeader("Accept", "application/json");

    int status = 0;
    if (request.method == HttpMethod::Get) {
        status = http.GET();
    } else {
        http.addHeader("Content-Type", "application/json");
        status = http.POST(reinterpret_cast<uint8_t*>(const_cast<char*>(request.body.data())), request.body.size());
    }

    if (status > 0) {
        response.status = status;
        response.retryAfterSeconds = parseRetryAfter(http.header("Retry-After").c_str());
        const int size = http.getSize();
        if (status == 200 && size <= kMaxResponseBody) {
            const String body = http.getString();
            if (body.length() <= static_cast<unsigned int>(kMaxResponseBody)) {
                response.body = body.c_str();
            }
        }
    }
    http.end();
    return response;
}

}  // namespace sciph

#endif
