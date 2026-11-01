#pragma once

#include <string>

namespace sciph {

// The USB serial console (provisioning prompts, user feedback and logs).
class IConsoleOutput {
public:
    virtual ~IConsoleOutput() = default;
    virtual void println(const std::string& line) = 0;
};

}  // namespace sciph
