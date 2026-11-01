#if defined(ARDUINO)

#include "network/wifi_link_esp32.h"

#include <WiFi.h>
#include <time.h>

#include "firmware_config.h"

namespace sciph {

void WifiNetworkLink::begin() {
    WiFi.mode(WIFI_STA);
    WiFi.setAutoReconnect(false);
    WiFi.begin(config_.wifiSsid.c_str(), config_.wifiPassword.c_str());
}

bool WifiNetworkLink::connected() const { return WiFi.status() == WL_CONNECTED; }

void WifiNetworkLink::reconnect() {
    WiFi.disconnect();
    WiFi.begin(config_.wifiSsid.c_str(), config_.wifiPassword.c_str());
}

void WifiNetworkLink::startTimeSync() {
    if (!timeSyncStarted_) {
        configTime(0, 0, SCIPH_NTP_SERVER_1, SCIPH_NTP_SERVER_2);
        timeSyncStarted_ = true;
    }
}

}  // namespace sciph

#endif
