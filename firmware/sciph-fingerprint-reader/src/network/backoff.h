#pragma once

#include <cstdint>

namespace sciph {

// 1 s, 2 s, 4 s, 8 s... capped (30 s for Wi-Fi). reset() after a success.
class ExponentialBackoff {
public:
    ExponentialBackoff(uint32_t initialMs, uint32_t maxMs) : initialMs_(initialMs), maxMs_(maxMs), nextMs_(initialMs) {}

    uint32_t nextDelayMs() {
        const uint32_t delay = nextMs_;
        nextMs_ = nextMs_ >= maxMs_ / 2 ? maxMs_ : nextMs_ * 2;
        return delay;
    }

    void reset() { nextMs_ = initialMs_; }

private:
    uint32_t initialMs_;
    uint32_t maxMs_;
    uint32_t nextMs_;
};

}  // namespace sciph
