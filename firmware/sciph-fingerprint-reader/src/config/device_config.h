#pragma once

#include <cstdint>
#include <string>

#include "storage/key_value_store.h"

namespace sciph {

enum class TransportMode : uint8_t {
    // HTTPS validated against the provisioned CA. The only mode of a production build.
    Https = 0,
    // Plain HTTP to a Device API with RequireHttps=false on an isolated network (esp32dev-devhttp builds only).
    DevelopmentHttp = 1
};

// Provisioned over Serial, stored in NVS, never committed or logged.
struct DeviceConfig {
    std::string wifiSsid;
    std::string wifiPassword;
    std::string apiBaseUrl;
    std::string deviceCode;
    std::string deviceKey;
    std::string caCertificatePem;
    TransportMode transport = TransportMode::Https;
};

enum class ConfigError {
    None,
    WifiSsid,
    WifiPassword,
    ApiUrl,
    DeviceCode,
    DeviceKey,
    CaCertificate,
    DevelopmentHttpNotAllowed
};

// True only in builds with SCIPH_ALLOW_DEV_HTTP=1.
bool developmentHttpBuild();

ConfigError validateConfig(const DeviceConfig& config, bool developmentHttpAllowed);
const char* configErrorMessage(ConfigError error);

// Trims spaces and trailing slashes.
std::string normalizeBaseUrl(const std::string& url);
bool startsWith(const std::string& text, const char* prefix);

enum class ConfigLoadResult { NotProvisioned, Loaded, Invalid };

// The configuration in NVS. A save writes the "complete" marker last and a forget removes it first, so a power cut in
// the middle never leaves a half-written configuration that looks provisioned.
class ConfigRepository {
public:
    explicit ConfigRepository(IKeyValueStore& store) : store_(store) {}

    ConfigLoadResult load(DeviceConfig& config, bool developmentHttpAllowed, ConfigError& error);
    bool save(const DeviceConfig& config);
    // Removes the credential, Wi-Fi, API URL and CA. The enrollment journal and the deletion attempt are kept: they
    // protect slots on the sensor and are resolved by the next provisioning of the same reader.
    bool forget();

private:
    IKeyValueStore& store_;
};

}  // namespace sciph
