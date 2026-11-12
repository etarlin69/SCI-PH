#pragma once

#include <functional>
#include <string>
#include <vector>

#include "config/device_config.h"
#include "core/console_output.h"

namespace sciph {

struct ConsoleHooks {
    std::function<std::vector<std::string>()> statusLines;
    std::function<void(bool allowLargeDeletion)> reconcile;
    std::function<bool()> discardUnreadableJournal;
    std::function<void()> restart;
};

// Line-based serial commands:
//   provision          guided setup: Wi-Fi SSID and password, Device API URL, device code, device key, CA (PEM)
//   status             state, configuration without secrets, sensor and journal summary
//   forget             removes credential, Wi-Fi, URL and CA (asks for confirmation); never touches sensor templates
//   reconcile [force]  runs the orphan slot reconciliation now
//   journal discard    discards an UNREADABLE enrollment journal (only that case)
//   restart, help
// Secrets are never echoed or printed: the status shows only whether they are set.
class SerialConsole {
public:
    SerialConsole(IConsoleOutput& output, ConfigRepository& configs, const DeviceConfig& current, bool provisioned,
                  bool developmentHttpAllowed, ConsoleHooks hooks);

    void handleLine(const std::string& rawLine);
    bool provisioningActive() const { return step_ != Step::None && step_ != Step::ForgetConfirm; }

private:
    enum class Step {
        None,
        WifiSsid,
        WifiPassword,
        ApiUrl,
        DeviceCode,
        DeviceKey,
        CaCertificate,
        Confirm,
        ForgetConfirm
    };

    void handleCommand(const std::string& line);
    void handleProvisioning(const std::string& line);
    void prompt(Step step);
    void printStatus();
    void printHelp();
    void cancel();
    void clearDraft();

    IConsoleOutput& output_;
    ConfigRepository& configs_;
    const DeviceConfig& current_;
    bool provisioned_;
    bool developmentHttpAllowed_;
    ConsoleHooks hooks_;
    Step step_ = Step::None;
    DeviceConfig draft_;
};

}  // namespace sciph
