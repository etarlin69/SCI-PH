#pragma once

#if defined(ARDUINO)

#include <WiFiClient.h>
#include <WiFiClientSecure.h>

#include "api/http_transport.h"
#include "config/device_config.h"

namespace sciph {

// HTTPClient over WiFiClientSecure validated against the provisioned CA (setCACert). setInsecure() is never used.
// Plain WiFiClient only for a DevelopmentHttp configuration in a build with SCIPH_ALLOW_DEV_HTTP=1.
class Esp32HttpTransport : public IHttpTransport {
public:
    Esp32HttpTransport(const DeviceConfig& config, uint32_t timeoutMs) : config_(config), timeoutMs_(timeoutMs) {}

    HttpResponse send(const HttpRequest& request) override;

private:
    const DeviceConfig& config_;
    uint32_t timeoutMs_;
    WiFiClientSecure secureClient_;
    WiFiClient plainClient_;
    bool caLoaded_ = false;
};

}  // namespace sciph

#endif
