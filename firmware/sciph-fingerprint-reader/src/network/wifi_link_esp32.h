#pragma once

#if defined(ARDUINO)

#include "config/device_config.h"
#include "network/network_link.h"

namespace sciph {

// Wi-Fi station with reconnection driven by the controller (exponential backoff), plus SNTP in UTC.
class WifiNetworkLink : public INetworkLink {
public:
    explicit WifiNetworkLink(const DeviceConfig& config) : config_(config) {}

    void begin() override;
    bool connected() const override;
    void reconnect() override;
    void startTimeSync() override;

private:
    const DeviceConfig& config_;
    bool timeSyncStarted_ = false;
};

}  // namespace sciph

#endif
