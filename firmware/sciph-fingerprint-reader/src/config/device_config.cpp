#include "config/device_config.h"

#include "firmware_config.h"

namespace sciph {

namespace {

constexpr const char* kSpace = "sciph-cfg";
constexpr const char* kComplete = "complete";
constexpr const char* kSsid = "wifi-ssid";
constexpr const char* kPassword = "wifi-pass";
constexpr const char* kApiUrl = "api-url";
constexpr const char* kDeviceCode = "dev-code";
constexpr const char* kDeviceKey = "dev-key";
constexpr const char* kCaPem = "ca-pem";
constexpr const char* kTransport = "transport";

constexpr size_t kMaxDeviceCode = 60;
constexpr size_t kMinDeviceKey = 16;
constexpr size_t kMaxDeviceKey = 256;
constexpr size_t kMaxUrl = 200;
// NVS strings are limited to about 4000 bytes: one root CA in PEM fits comfortably.
constexpr size_t kMaxCaPem = 3800;

bool visibleAscii(const std::string& value) {
    for (char c : value) {
        if (c < 0x21 || c > 0x7E) {
            return false;
        }
    }
    return true;
}

bool validUrl(const std::string& url, const char* scheme) {
    if (url.size() > kMaxUrl || !startsWith(url, scheme) || !visibleAscii(url)) {
        return false;
    }
    const std::string rest = url.substr(std::string(scheme).size());
    return !rest.empty() && rest[0] != '/' && rest.find('?') == std::string::npos &&
           rest.find('#') == std::string::npos && rest.find('@') == std::string::npos;
}

}  // namespace

bool developmentHttpBuild() { return SCIPH_ALLOW_DEV_HTTP != 0; }

bool startsWith(const std::string& text, const char* prefix) {
    const std::string value(prefix);
    return text.size() >= value.size() && text.compare(0, value.size(), value) == 0;
}

std::string normalizeBaseUrl(const std::string& url) {
    size_t start = 0;
    size_t end = url.size();
    while (start < end && (url[start] == ' ' || url[start] == '\t')) {
        ++start;
    }
    while (end > start && (url[end - 1] == ' ' || url[end - 1] == '\t' || url[end - 1] == '/')) {
        --end;
    }
    return url.substr(start, end - start);
}

ConfigError validateConfig(const DeviceConfig& config, bool developmentHttpAllowed) {
    if (config.wifiSsid.empty() || config.wifiSsid.size() > 32) {
        return ConfigError::WifiSsid;
    }
    if (config.wifiPassword.size() > 64) {
        return ConfigError::WifiPassword;
    }
    if (config.deviceCode.empty() || config.deviceCode.size() > kMaxDeviceCode || !visibleAscii(config.deviceCode)) {
        return ConfigError::DeviceCode;
    }
    if (config.deviceKey.size() < kMinDeviceKey || config.deviceKey.size() > kMaxDeviceKey ||
        !visibleAscii(config.deviceKey)) {
        return ConfigError::DeviceKey;
    }
    if (config.transport == TransportMode::DevelopmentHttp) {
        if (!developmentHttpAllowed) {
            return ConfigError::DevelopmentHttpNotAllowed;
        }
        return validUrl(config.apiBaseUrl, "http://") ? ConfigError::None : ConfigError::ApiUrl;
    }
    if (!validUrl(config.apiBaseUrl, "https://")) {
        return ConfigError::ApiUrl;
    }
    if (config.caCertificatePem.size() > kMaxCaPem ||
        config.caCertificatePem.find("-----BEGIN CERTIFICATE-----") == std::string::npos ||
        config.caCertificatePem.find("-----END CERTIFICATE-----") == std::string::npos) {
        return ConfigError::CaCertificate;
    }
    return ConfigError::None;
}

const char* configErrorMessage(ConfigError error) {
    switch (error) {
        case ConfigError::None:
            return "configuracion valida";
        case ConfigError::WifiSsid:
            return "SSID de Wi-Fi invalido (1-32 caracteres)";
        case ConfigError::WifiPassword:
            return "clave de Wi-Fi invalida (maximo 64 caracteres)";
        case ConfigError::ApiUrl:
            return "URL del Device API invalida (https://host[:puerto], sin ruta de consulta ni credenciales)";
        case ConfigError::DeviceCode:
            return "codigo de dispositivo invalido (1-60 caracteres visibles)";
        case ConfigError::DeviceKey:
            return "clave de dispositivo invalida (16-256 caracteres visibles)";
        case ConfigError::CaCertificate:
            return "certificado CA invalido (un PEM -----BEGIN/END CERTIFICATE-----)";
        case ConfigError::DevelopmentHttpNotAllowed:
            return "HTTP de desarrollo no permitido en esta compilacion";
    }
    return "configuracion invalida";
}

ConfigLoadResult ConfigRepository::load(DeviceConfig& config, bool developmentHttpAllowed, ConfigError& error) {
    error = ConfigError::None;
    std::string complete;
    if (!store_.get(kSpace, kComplete, complete) || complete != "1") {
        return ConfigLoadResult::NotProvisioned;
    }

    DeviceConfig loaded;
    std::string transport;
    store_.get(kSpace, kSsid, loaded.wifiSsid);
    store_.get(kSpace, kPassword, loaded.wifiPassword);
    store_.get(kSpace, kApiUrl, loaded.apiBaseUrl);
    store_.get(kSpace, kDeviceCode, loaded.deviceCode);
    store_.get(kSpace, kDeviceKey, loaded.deviceKey);
    store_.get(kSpace, kCaPem, loaded.caCertificatePem);
    store_.get(kSpace, kTransport, transport);
    loaded.transport = transport == "dev-http" ? TransportMode::DevelopmentHttp : TransportMode::Https;

    error = validateConfig(loaded, developmentHttpAllowed);
    if (error != ConfigError::None) {
        return ConfigLoadResult::Invalid;
    }
    config = loaded;
    return ConfigLoadResult::Loaded;
}

bool ConfigRepository::save(const DeviceConfig& config) {
    store_.remove(kSpace, kComplete);
    const bool written =
        store_.put(kSpace, kSsid, config.wifiSsid) && store_.put(kSpace, kPassword, config.wifiPassword) &&
        store_.put(kSpace, kApiUrl, config.apiBaseUrl) && store_.put(kSpace, kDeviceCode, config.deviceCode) &&
        store_.put(kSpace, kDeviceKey, config.deviceKey) &&
        store_.put(kSpace, kCaPem, config.transport == TransportMode::Https ? config.caCertificatePem : std::string()) &&
        store_.put(kSpace, kTransport, config.transport == TransportMode::DevelopmentHttp ? "dev-http" : "https");
    return written && store_.put(kSpace, kComplete, "1");
}

bool ConfigRepository::forget() {
    bool removed = store_.remove(kSpace, kComplete);
    removed = store_.remove(kSpace, kDeviceKey) && removed;
    removed = store_.remove(kSpace, kPassword) && removed;
    removed = store_.remove(kSpace, kSsid) && removed;
    removed = store_.remove(kSpace, kApiUrl) && removed;
    removed = store_.remove(kSpace, kDeviceCode) && removed;
    removed = store_.remove(kSpace, kCaPem) && removed;
    removed = store_.remove(kSpace, kTransport) && removed;
    return removed;
}

}  // namespace sciph
