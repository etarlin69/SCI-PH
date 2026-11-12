#pragma once

#include <string>

#include "core/console_output.h"

namespace sciph {

enum class LogLevel { Info, Warning, Error };

// Normal logs carry only: operation, a short correlation, state, HTTP status, error category and latency. Never slot
// numbers, templates, images, the device key, the Wi-Fi password, patient data or SubjectReference.
class ILogger {
public:
    virtual ~ILogger() = default;
    virtual void write(LogLevel level, const char* operation, const std::string& message) = 0;
};

// Writes "[level] operation: message" to the serial console.
class ConsoleLogger : public ILogger {
public:
    explicit ConsoleLogger(IConsoleOutput& output) : output_(output) {}
    void write(LogLevel level, const char* operation, const std::string& message) override;

private:
    IConsoleOutput& output_;
};

const char* logLevelName(LogLevel level);

// First 8 characters of a server id: enough to correlate with the server logs.
std::string shortId(const std::string& id);

// Slot numbers only in explicit diagnostic builds (SCIPH_DEBUG_SLOTS=1); a no-op otherwise.
void debugSlot(ILogger& logger, const char* operation, const char* what, int slot);

}  // namespace sciph
