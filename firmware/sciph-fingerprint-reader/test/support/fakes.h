#pragma once

// Simulated clock, sensor, HTTP, NVS, network, feedback and logs for the native tests. Header-only so that every test
// suite includes it directly.

#include <unity.h>

#include <cstdio>
#include <deque>
#include <functional>
#include <initializer_list>
#include <map>
#include <string>
#include <vector>

#include "api/http_transport.h"
#include "core/clock.h"
#include "core/console_output.h"
#include "core/logger.h"
#include "core/uuid.h"
#include "feedback/feedback.h"
#include "fingerprint/r307/fingerprint_sensor.h"
#include "network/network_link.h"
#include "storage/key_value_store.h"

#define ASSERT_ENUM(expected, actual) TEST_ASSERT_EQUAL_INT(static_cast<int>(expected), static_cast<int>(actual))

namespace sciph {
namespace testing {

class FakeClock : public IClock {
public:
    uint64_t now = 1000;
    bool utcValid = true;
    int64_t utc = 1798761600;  // 2027-01-01

    uint64_t monotonicMs() const override { return now; }
    bool utcNow(int64_t& epochSeconds) const override {
        epochSeconds = utcValid ? utc + static_cast<int64_t>(now / 1000) : 0;
        return utcValid;
    }
    void sleepMs(uint32_t ms) override { now += ms; }
    void advance(uint64_t ms) { now += ms; }
};

class CapturingOutput : public IConsoleOutput {
public:
    std::vector<std::string> lines;
    void println(const std::string& line) override { lines.push_back(line); }
    std::string all() const {
        std::string text;
        for (const std::string& line : lines) {
            text += line + "\n";
        }
        return text;
    }
    bool contains(const std::string& fragment) const { return all().find(fragment) != std::string::npos; }
};

class CapturingLogger : public ILogger {
public:
    std::vector<std::string> lines;
    void write(LogLevel level, const char* operation, const std::string& message) override {
        lines.push_back(std::string(logLevelName(level)) + " " + operation + ": " + message);
    }
    std::string all() const {
        std::string text;
        for (const std::string& line : lines) {
            text += line + "\n";
        }
        return text;
    }
    bool contains(const std::string& fragment) const { return all().find(fragment) != std::string::npos; }
};

class RecordingFeedback : public IFeedback {
public:
    std::vector<FeedbackSignal> signals;
    void signal(FeedbackSignal signal) override { signals.push_back(signal); }
    bool saw(FeedbackSignal signal) const {
        for (FeedbackSignal s : signals) {
            if (s == signal) {
                return true;
            }
        }
        return false;
    }
};

class MemoryStore : public IKeyValueStore {
public:
    std::map<std::string, std::string> values;
    bool failPuts = false;

    static std::string id(const char* space, const char* key) { return std::string(space) + "/" + key; }
    bool get(const char* space, const char* key, std::string& value) override {
        auto found = values.find(id(space, key));
        if (found == values.end()) {
            return false;
        }
        value = found->second;
        return true;
    }
    bool put(const char* space, const char* key, const std::string& value) override {
        if (failPuts) {
            return false;
        }
        values[id(space, key)] = value;
        return true;
    }
    bool remove(const char* space, const char* key) override {
        values.erase(id(space, key));
        return true;
    }
    bool has(const char* space, const char* key) const { return values.count(id(space, key)) > 0; }
    std::string dump() const {
        std::string text;
        for (const auto& entry : values) {
            text += entry.first + "=" + entry.second + "\n";
        }
        return text;
    }
};

class SequenceRandom : public IRandomSource {
public:
    uint32_t value = 0x12345678;
    uint32_t next32() override {
        value = value * 1664525u + 1013904223u;
        return value;
    }
};

class FakeNetwork : public INetworkLink {
public:
    bool up = true;
    int begins = 0;
    int reconnects = 0;
    int timeSyncs = 0;
    std::vector<uint64_t> reconnectTimes;
    FakeClock* clock = nullptr;

    void begin() override { ++begins; }
    bool connected() const override { return up; }
    void reconnect() override {
        ++reconnects;
        if (clock != nullptr) {
            reconnectTimes.push_back(clock->now);
        }
    }
    void startTimeSync() override { ++timeSyncs; }
};

inline HttpResponse respond(int status, const std::string& body = "", int retryAfter = -1) {
    HttpResponse response;
    response.status = status;
    response.body = body;
    response.retryAfterSeconds = retryAfter;
    return response;
}

// Answers by "METHOD path": queued responses first, then a per-route default, then the global default.
class FakeTransport : public IHttpTransport {
public:
    std::vector<HttpRequest> requests;
    std::map<std::string, std::deque<HttpResponse>> queued;
    std::map<std::string, HttpResponse> defaults;
    HttpResponse fallback = respond(204);
    bool offline = false;
    std::function<void(const HttpRequest&)> onSend;

    static std::string key(HttpMethod method, const std::string& path) {
        return std::string(method == HttpMethod::Get ? "GET " : "POST ") + path;
    }
    void queue(HttpMethod method, const std::string& path, HttpResponse response) {
        queued[key(method, path)].push_back(response);
    }
    void setDefault(HttpMethod method, const std::string& path, HttpResponse response) {
        defaults[key(method, path)] = response;
    }

    HttpResponse send(const HttpRequest& request) override {
        requests.push_back(request);
        if (onSend) {
            onSend(request);
        }
        if (offline) {
            return respond(0);
        }
        const std::string route = key(request.method, request.path);
        auto pending = queued.find(route);
        if (pending != queued.end() && !pending->second.empty()) {
            HttpResponse response = pending->second.front();
            pending->second.pop_front();
            return response;
        }
        auto fixed = defaults.find(route);
        if (fixed != defaults.end()) {
            return fixed->second;
        }
        return fallback;
    }

    size_t count(HttpMethod method, const std::string& path) const {
        size_t total = 0;
        for (const HttpRequest& request : requests) {
            if (request.method == method && request.path == path) {
                ++total;
            }
        }
        return total;
    }
    std::vector<std::string> bodies(HttpMethod method, const std::string& path) const {
        std::vector<std::string> result;
        for (const HttpRequest& request : requests) {
            if (request.method == method && request.path == path) {
                result.push_back(request.body);
            }
        }
        return result;
    }
};

// A simulated R307: templates per slot, scripted captures and injectable failures.
class FakeSensor : public IFingerprintSensor {
public:
    explicit FakeSensor(uint16_t capacity = 20) : templates(capacity, false), capacity_(capacity) {}

    std::vector<bool> templates;
    bool responding = true;
    bool indexTableSupported = false;
    bool indexTableLsbFirst = true;
    std::deque<SensorStatus> images;           // captureImage script
    SensorStatus imageDefault = SensorStatus::NoFinger;
    std::deque<SensorStatus> templatesScript;  // imageToTemplate script
    SensorStatus createModelResult = SensorStatus::Ok;
    SearchResult searchResult{SensorStatus::NoMatch, -1};
    SensorStatus storeResult = SensorStatus::Ok;
    SensorStatus deleteResult = SensorStatus::Ok;
    std::map<int, SensorStatus> probeOverrides;
    std::function<void(int)> beforeStore;
    std::function<void(int)> beforeProbe;
    int storeCalls = 0;
    int deleteCalls = 0;
    int probeCalls = 0;
    std::vector<int> deleted;
    std::vector<int> stored;

    void occupy(int slot) { templates[static_cast<size_t>(slot)] = true; }
    bool occupied(int slot) const { return templates[static_cast<size_t>(slot)]; }
    void script(std::initializer_list<SensorStatus> sequence) {
        for (SensorStatus status : sequence) {
            images.push_back(status);
        }
    }

    SensorStatus connect() override { return responding ? SensorStatus::Ok : SensorStatus::CommunicationError; }
    SensorStatus readParameters(SensorParameters& parameters) override {
        if (!responding) {
            return SensorStatus::CommunicationError;
        }
        parameters.capacity = capacity_;
        parameters.securityLevel = 3;
        parameters.packetLength = 128;
        parameters.baudRate = 57600;
        return SensorStatus::Ok;
    }
    SensorStatus captureImage() override {
        if (!responding) {
            return SensorStatus::CommunicationError;
        }
        if (images.empty()) {
            return imageDefault;
        }
        SensorStatus status = images.front();
        images.pop_front();
        return status;
    }
    SensorStatus imageToTemplate(uint8_t) override {
        if (!responding) {
            return SensorStatus::CommunicationError;
        }
        if (templatesScript.empty()) {
            return SensorStatus::Ok;
        }
        SensorStatus status = templatesScript.front();
        templatesScript.pop_front();
        return status;
    }
    SensorStatus createModel() override { return responding ? createModelResult : SensorStatus::CommunicationError; }
    SensorStatus storeModel(int slot) override {
        ++storeCalls;
        if (beforeStore) {
            beforeStore(slot);
        }
        if (!responding) {
            return SensorStatus::CommunicationError;
        }
        if (slot < 0 || slot >= static_cast<int>(capacity_)) {
            return SensorStatus::OutOfRange;
        }
        if (storeResult == SensorStatus::Ok) {
            templates[static_cast<size_t>(slot)] = true;
            stored.push_back(slot);
        }
        return storeResult;
    }
    SensorStatus probeSlot(int slot) override {
        ++probeCalls;
        if (beforeProbe) {
            beforeProbe(slot);
        }
        if (!responding) {
            return SensorStatus::CommunicationError;
        }
        if (slot < 0 || slot >= static_cast<int>(capacity_)) {
            return SensorStatus::OutOfRange;
        }
        auto forced = probeOverrides.find(slot);
        if (forced != probeOverrides.end()) {
            return forced->second;
        }
        return templates[static_cast<size_t>(slot)] ? SensorStatus::Occupied : SensorStatus::Empty;
    }
    SensorStatus deleteModel(int slot) override {
        ++deleteCalls;
        if (!responding) {
            return SensorStatus::CommunicationError;
        }
        if (deleteResult == SensorStatus::Ok && slot >= 0 && slot < static_cast<int>(capacity_)) {
            templates[static_cast<size_t>(slot)] = false;
            deleted.push_back(slot);
        }
        return deleteResult;
    }
    SearchResult search() override {
        if (!responding) {
            return SearchResult{SensorStatus::CommunicationError, -1};
        }
        return searchResult;
    }
    SensorStatus templateCount(uint16_t& count) override {
        if (!responding) {
            return SensorStatus::CommunicationError;
        }
        count = 0;
        for (bool used : templates) {
            count = static_cast<uint16_t>(count + (used ? 1 : 0));
        }
        return SensorStatus::Ok;
    }
    SensorStatus readIndexPage(uint8_t page, uint8_t* bits32) override {
        if (!indexTableSupported) {
            return SensorStatus::Unsupported;
        }
        for (int i = 0; i < 32; ++i) {
            bits32[i] = 0;
        }
        for (int offset = 0; offset < 256; ++offset) {
            const int slot = page * 256 + offset;
            if (slot < static_cast<int>(capacity_) && templates[static_cast<size_t>(slot)]) {
                const int bit = indexTableLsbFirst ? offset % 8 : 7 - offset % 8;
                bits32[offset / 8] = static_cast<uint8_t>(bits32[offset / 8] | (1 << bit));
            }
        }
        return SensorStatus::Ok;
    }

private:
    uint16_t capacity_;
};

// A well-formed UUID per number, for readable tests.
inline std::string uuid(int n) {
    char text[37];
    snprintf(text, sizeof(text), "%08x-0000-4000-8000-%012x", n, n);
    return std::string(text);
}

inline bool containsText(const std::string& haystack, const std::string& needle) {
    return haystack.find(needle) != std::string::npos;
}

}  // namespace testing
}  // namespace sciph
