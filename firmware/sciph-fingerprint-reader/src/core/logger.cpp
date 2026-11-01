#include "core/logger.h"

#include "firmware_config.h"

namespace sciph {

void ConsoleLogger::write(LogLevel level, const char* operation, const std::string& message) {
    output_.println(std::string("[") + logLevelName(level) + "] " + operation + ": " + message);
}

const char* logLevelName(LogLevel level) {
    switch (level) {
        case LogLevel::Info:
            return "info";
        case LogLevel::Warning:
            return "warn";
        case LogLevel::Error:
            return "error";
    }
    return "info";
}

std::string shortId(const std::string& id) { return id.substr(0, 8); }

void debugSlot(ILogger& logger, const char* operation, const char* what, int slot) {
#if SCIPH_DEBUG_SLOTS
    logger.write(LogLevel::Info, operation, std::string("[debug-slots] ") + what + " " + std::to_string(slot));
#else
    (void)logger;
    (void)operation;
    (void)what;
    (void)slot;
#endif
}

}  // namespace sciph
