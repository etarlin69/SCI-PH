#pragma once

namespace sciph {

// Wi-Fi station plus SNTP. The ESP32 adapter wraps WiFi/configTime; native tests use a fake.
class INetworkLink {
public:
    virtual ~INetworkLink() = default;
    virtual void begin() = 0;
    virtual bool connected() const = 0;
    virtual void reconnect() = 0;
    virtual void startTimeSync() = 0;
};

}  // namespace sciph
