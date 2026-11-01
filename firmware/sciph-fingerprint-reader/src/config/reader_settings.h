#pragma once

#include <cstddef>
#include <cstdint>

namespace sciph {

// Physical capture on the R307.
struct CaptureSettings {
    // How long the reader waits for a finger before giving up on this attempt (no result is sent: the session or
    // enrollment stays Pending on the server and the next poll prompts again until it expires there).
    uint32_t fingerTimeoutMs = 20000;
    uint32_t removeTimeoutMs = 15000;
    // Poor captures retried locally before reporting LowQuality.
    uint8_t maxLowQualityAttempts = 3;
    // Consecutive UART failures before reporting SensorError.
    uint8_t maxCommunicationErrors = 3;
    uint32_t pollIntervalMs = 100;
};

// Re-sending the SAME result after an uncertain answer (no response, timeout, 5xx, 429).
struct ReplaySettings {
    uint32_t initialDelayMs = 2000;
    uint32_t maxDelayMs = 30000;
    // Identification and failed-enrollment results (no slot on the sensor depends on them) are dropped after this:
    // the server expires their session anyway. A stored enrollment is never dropped: only the server decides it.
    uint32_t abandonAfterMs = 180000;
};

// Recommended polling (README, "Polling"): about 43 requests per minute, under the 120/60 s device limit.
struct SchedulerSettings {
    uint32_t identificationPollMs = 3000;
    uint32_t enrollmentPollMs = 3000;
    uint32_t deletionPollMs = 30000;
    uint32_t heartbeatMs = 60000;
    // Optional periodic reconciliation (0 = only at boot, after a recovered journal and on the serial command).
    uint32_t reconciliationIntervalMs = 6UL * 60UL * 60UL * 1000UL;
    uint32_t reconciliationRetryMs = 60000;
    // 403: a configuration problem; the scope family is not retried before this.
    uint32_t forbiddenRetryMs = 300000;
    // 429 without a usable Retry-After.
    uint32_t defaultRetryAfterMs = 10000;
    uint32_t maxRetryAfterMs = 300000;
};

struct ReconciliationSettings {
    // More orphan candidates than this in one run (for example a sensor provisioned under another device code) are
    // not deleted automatically: the operator confirms with "reconcile force".
    size_t maxAutomaticDeletions = 10;
};

struct ControllerSettings {
    uint32_t wifiConnectTimeoutMs = 20000;
    uint32_t wifiBackoffInitialMs = 1000;
    uint32_t wifiBackoffMaxMs = 30000;
    uint32_t timeSyncWarningMs = 15000;
    uint32_t sensorRetryMs = 10000;
    uint32_t authRetryMs = 300000;
    // Larger reported capacities are treated as a misread.
    uint16_t maxCapacity = 5000;
    // After a failed deletion attempt was accepted, the next physical attempt waits this long (new attemptId).
    uint32_t deletionSensorErrorRetryMs = 30000;
};

}  // namespace sciph
