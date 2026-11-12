// Identification: Match + slot, NoMatch, LowQuality, SensorError, no score, same-result replay and 404/409 stop.
#include <unity.h>

#include "../support/fakes.h"
#include "api/device_api_client.h"
#include "fingerprint/identification/identification_coordinator.h"

using namespace sciph;
using namespace sciph::testing;

void setUp() {}
void tearDown() {}

namespace {

const std::string kSessionId = uuid(21);

struct Harness {
    FakeClock clock;
    FakeTransport transport;
    FakeSensor sensor{1000};
    RecordingFeedback feedback;
    CapturingLogger logger;
    DeviceApiClient api{transport, clock};
    CaptureSettings capture;
    ReplaySettings replay;
    IdentificationCoordinator coordinator{api, sensor, feedback, clock, logger, capture, replay};

    void pendingSession() {
        transport.queue(HttpMethod::Get, device_paths::kPendingSession,
                        respond(200, "{\"sessionId\":\"" + kSessionId + "\",\"expiresAtUtc\":\"2027-01-01T00:01:30+00:00\"}"));
    }
    std::string resultPath() const { return device_paths::sessionResult(kSessionId); }
    std::vector<std::string> bodies() const { return transport.bodies(HttpMethod::Post, resultPath()); }
};

}  // namespace

static void test_no_pending_session_does_nothing_physical() {
    Harness h;
    h.transport.queue(HttpMethod::Get, device_paths::kPendingSession, respond(204));
    const OperationReport report = h.coordinator.poll();
    ASSERT_ENUM(ApiStatus::NoContent, report.api.status);
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(h.transport.requests.size()));
    TEST_ASSERT_FALSE(h.feedback.saw(FeedbackSignal::PlaceFinger));
}

static void test_match_reports_the_slot_without_score() {
    Harness h;
    h.pendingSession();
    h.sensor.script({SensorStatus::Ok});
    h.sensor.searchResult = SearchResult{SensorStatus::Ok, 737};
    h.coordinator.poll();
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"Match\",\"slot\":737}", h.bodies()[0].c_str());
    TEST_ASSERT_FALSE(h.coordinator.replayPending());
    TEST_ASSERT_TRUE(h.feedback.saw(FeedbackSignal::Success));
    TEST_ASSERT_FALSE(h.logger.contains("737"));
}

static void test_no_match() {
    Harness h;
    h.pendingSession();
    h.sensor.script({SensorStatus::Ok});
    h.sensor.searchResult = SearchResult{SensorStatus::NoMatch, -1};
    h.coordinator.poll();
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"NoMatch\"}", h.bodies()[0].c_str());
    TEST_ASSERT_TRUE(h.feedback.saw(FeedbackSignal::NotRecognized));
}

static void test_low_quality_after_local_retries() {
    Harness h;
    h.pendingSession();
    h.sensor.templatesScript = {SensorStatus::LowQuality, SensorStatus::LowQuality, SensorStatus::LowQuality};
    h.sensor.script({SensorStatus::Ok, SensorStatus::NoFinger, SensorStatus::Ok, SensorStatus::NoFinger, SensorStatus::Ok});
    h.coordinator.poll();
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"LowQuality\"}", h.bodies()[0].c_str());
}

static void test_sensor_error_is_reported_and_flagged() {
    Harness h;
    h.pendingSession();
    h.sensor.script({SensorStatus::CommunicationError, SensorStatus::CommunicationError, SensorStatus::CommunicationError});
    const OperationReport report = h.coordinator.poll();
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"SensorError\"}", h.bodies()[0].c_str());
    TEST_ASSERT_TRUE(report.sensorFault);
}

static void test_capture_timeout_sends_no_result() {
    Harness h;
    h.pendingSession();
    h.coordinator.poll();
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(h.bodies().size()));
    TEST_ASSERT_FALSE(h.coordinator.replayPending());
}

static void test_uncertain_answer_replays_the_exact_result() {
    Harness h;
    h.pendingSession();
    h.sensor.script({SensorStatus::Ok});
    h.sensor.searchResult = SearchResult{SensorStatus::Ok, 12};
    h.transport.queue(HttpMethod::Post, h.resultPath(), respond(0));
    h.transport.queue(HttpMethod::Post, h.resultPath(), respond(429, "", 3));
    h.coordinator.poll();
    TEST_ASSERT_TRUE(h.coordinator.replayPending());
    TEST_ASSERT_FALSE(h.coordinator.replayDue(h.clock.now));
    h.clock.advance(h.replay.initialDelayMs);
    TEST_ASSERT_TRUE(h.coordinator.replayDue(h.clock.now));
    const OperationReport throttled = h.coordinator.replay();
    ASSERT_ENUM(ApiStatus::RateLimited, throttled.api.status);
    TEST_ASSERT_EQUAL_INT(3, throttled.api.retryAfterSeconds);
    h.clock.advance(h.replay.maxDelayMs);
    h.coordinator.replay();
    const std::vector<std::string> bodies = h.bodies();
    TEST_ASSERT_EQUAL_INT(3, static_cast<int>(bodies.size()));
    for (const std::string& body : bodies) {
        TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"Match\",\"slot\":12}", body.c_str());
    }
    TEST_ASSERT_FALSE(h.coordinator.replayPending());
    // One physical capture only.
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(h.transport.count(HttpMethod::Get, device_paths::kPendingSession)));
}

static void test_404_and_409_stop_processing_the_session() {
    const int statuses[] = {404, 409};
    for (int status : statuses) {
        Harness h;
        h.pendingSession();
        h.sensor.script({SensorStatus::Ok});
        h.transport.queue(HttpMethod::Post, h.resultPath(), respond(status));
        h.coordinator.poll();
        TEST_ASSERT_FALSE(h.coordinator.replayPending());
        TEST_ASSERT_EQUAL_INT(1, static_cast<int>(h.bodies().size()));
    }
}

static void test_unconfirmed_result_is_abandoned_after_the_session_lifetime() {
    Harness h;
    h.pendingSession();
    h.sensor.script({SensorStatus::Ok});
    h.transport.setDefault(HttpMethod::Post, h.resultPath(), respond(503));
    h.coordinator.poll();
    h.clock.advance(h.replay.abandonAfterMs + 1);
    h.coordinator.replay();
    TEST_ASSERT_FALSE(h.coordinator.replayPending());
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(h.bodies().size()));
}

int main(int, char**) {
    UNITY_BEGIN();
    RUN_TEST(test_no_pending_session_does_nothing_physical);
    RUN_TEST(test_match_reports_the_slot_without_score);
    RUN_TEST(test_no_match);
    RUN_TEST(test_low_quality_after_local_retries);
    RUN_TEST(test_sensor_error_is_reported_and_flagged);
    RUN_TEST(test_capture_timeout_sends_no_result);
    RUN_TEST(test_uncertain_answer_replays_the_exact_result);
    RUN_TEST(test_404_and_409_stop_processing_the_session);
    RUN_TEST(test_unconfirmed_result_is_abandoned_after_the_session_lifetime);
    return UNITY_END();
}
