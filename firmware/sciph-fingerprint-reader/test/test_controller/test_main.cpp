// The reader state machine end to end with simulated hardware: boot order, provisioning, Wi-Fi backoff, time,
// sensor errors, AUTH_FAILED, 403, 429 and journal recovery at boot.
#include <unity.h>

#include "../support/fakes.h"
#include "api/device_api_client.h"
#include "fingerprint/cleanup/deletion_attempt.h"
#include "fingerprint/cleanup/deletion_coordinator.h"
#include "fingerprint/enrollment/enrollment_coordinator.h"
#include "fingerprint/identification/identification_coordinator.h"
#include "fingerprint/reconciliation/physical_slot_scanner.h"
#include "fingerprint/reconciliation/slot_reconciler.h"
#include "state/operation_scheduler.h"
#include "state/reader_controller.h"
#include "storage/journal/enrollment_journal.h"

using namespace sciph;
using namespace sciph::testing;

void setUp() {}
void tearDown() {}

namespace {

struct Harness {
    FakeClock clock;
    FakeTransport transport;
    FakeSensor sensor{50};
    MemoryStore store;
    SequenceRandom random;
    RecordingFeedback feedback;
    CapturingLogger logger;
    FakeNetwork network;
    DeviceApiClient api{transport, clock};
    SlotMap slots;
    ServerSlotSnapshot server;
    EnrollmentJournalStore journals{store};
    DeletionAttemptStore attempts{store};
    CaptureSettings capture;
    ReplaySettings replay;
    SchedulerSettings schedulerSettings;
    ReconciliationSettings reconciliationSettings;
    ControllerSettings controllerSettings;
    PhysicalSlotScanner scanner{sensor};
    SlotReconciler reconciler{api, sensor, slots, server, logger, reconciliationSettings};
    EnrollmentCoordinator enrollment{api, sensor, slots, server, journals, feedback, clock, logger, capture, replay};
    IdentificationCoordinator identification{api, sensor, feedback, clock, logger, capture, replay};
    DeletionCoordinator deletion{api,    sensor, slots,  attempts, random,
                                 clock,  logger, replay, controllerSettings.deletionSensorErrorRetryMs};
    OperationScheduler scheduler{schedulerSettings};
    ReaderController controller{ReaderDependencies{clock, logger, feedback, network, sensor, api, slots, server, scanner,
                                                   reconciler, enrollment, identification, deletion, scheduler},
                                controllerSettings, "0.1.0"};

    Harness() {
        network.clock = &clock;
        transport.setDefault(HttpMethod::Get, device_paths::kSlots, respond(200, "{\"slots\":[]}"));
    }

    void runFor(uint64_t ms) {
        const uint64_t end = clock.now + ms;
        while (clock.now < end) {
            controller.tick();
            clock.advance(10);
        }
    }

    bool runUntil(ReaderState state, uint64_t maxMs = 120000) {
        const uint64_t end = clock.now + maxMs;
        while (clock.now < end) {
            if (controller.state() == state) {
                return true;
            }
            controller.tick();
            clock.advance(10);
        }
        return controller.state() == state;
    }

    size_t requests(HttpMethod method, const std::string& path) const { return transport.count(method, path); }
};

size_t indexOf(const std::vector<std::string>& lines, const std::string& fragment) {
    for (size_t i = 0; i < lines.size(); ++i) {
        if (lines[i].find(fragment) != std::string::npos) {
            return i;
        }
    }
    return lines.size();
}

}  // namespace

static void test_unprovisioned_reader_waits_in_provisioning() {
    Harness h;
    h.controller.begin(false, true);
    h.runFor(5000);
    ASSERT_ENUM(ReaderState::Provisioning, h.controller.state());
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(h.transport.requests.size()));
    TEST_ASSERT_EQUAL_INT(0, h.network.begins);
    TEST_ASSERT_TRUE(h.feedback.saw(FeedbackSignal::Provisioning));
}

static void test_boot_order_and_no_polling_before_reconciliation() {
    Harness h;
    h.sensor.occupy(3);
    h.transport.setDefault(HttpMethod::Get, device_paths::kSlots, respond(200, "{\"slots\":[3]}"));
    size_t requestsDuringScan = 0;
    h.sensor.beforeProbe = [&](int) { requestsDuringScan += h.transport.requests.size(); };
    h.controller.begin(true, true);
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::Idle));
    h.sensor.beforeProbe = nullptr;

    const std::vector<std::string>& lines = h.logger.lines;
    const char* order[] = {"WIFI_CONNECT", "TIME_SYNC",          "SENSOR_INIT",       "READ_PARAMETERS",
                           "SLOT_RECONCILIATION", "JOURNAL_RECOVERY", "IDLE"};
    size_t previous = 0;
    for (const char* state : order) {
        const size_t position = indexOf(lines, std::string("state: ") + state);
        TEST_ASSERT_TRUE(position < lines.size());
        TEST_ASSERT_TRUE(position >= previous);
        previous = position;
    }
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(requestsDuringScan));
    TEST_ASSERT_EQUAL_STRING("GET /api/device/fingerprint/slots",
                             FakeTransport::key(h.transport.requests[0].method, h.transport.requests[0].path).c_str());
    TEST_ASSERT_EQUAL_INT(1, h.network.timeSyncs);
    TEST_ASSERT_TRUE(h.logger.contains("sensor capacity 50"));

    h.runFor(100);
    TEST_ASSERT_TRUE(h.requests(HttpMethod::Get, device_paths::kPendingSession) >= 1);
}

static void test_wifi_unavailable_uses_exponential_backoff_up_to_30_seconds() {
    Harness h;
    h.network.up = false;
    h.controller.begin(true, true);
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::NetworkRecovery, 30000));
    TEST_ASSERT_TRUE(h.feedback.saw(FeedbackSignal::NetworkError));
    h.runFor(130000);
    const std::vector<uint64_t>& times = h.network.reconnectTimes;
    TEST_ASSERT_TRUE(times.size() >= 7);
    const uint64_t expected[] = {2000, 4000, 8000, 16000, 30000, 30000};
    for (size_t i = 0; i < 6; ++i) {
        const uint64_t gap = times[i + 1] - times[i];
        TEST_ASSERT_TRUE(gap >= expected[i] && gap <= expected[i] + 10);
    }
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(h.transport.requests.size()));

    h.network.up = true;
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::Idle));
}

static void test_https_waits_for_time_and_development_http_continues() {
    Harness https;
    https.clock.utcValid = false;
    https.controller.begin(true, true);
    https.runFor(60000);
    ASSERT_ENUM(ReaderState::TimeSync, https.controller.state());
    TEST_ASSERT_TRUE(https.logger.contains("time_unavailable"));
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(https.transport.requests.size()));

    Harness development;
    development.clock.utcValid = false;
    development.controller.begin(true, false);
    TEST_ASSERT_TRUE(development.runUntil(ReaderState::Idle));
}

static void test_sensor_unavailable_enters_sensor_error_and_recovers() {
    Harness h;
    h.sensor.responding = false;
    h.controller.begin(true, true);
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::SensorError));
    TEST_ASSERT_TRUE(h.feedback.saw(FeedbackSignal::SensorError));
    h.runFor(30000);
    ASSERT_ENUM(ReaderState::SensorError, h.controller.state());
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(h.transport.requests.size()));

    h.sensor.responding = true;
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::Idle, 20000));
}

static void test_401_enters_auth_failed_and_stops_polling() {
    Harness h;
    h.controller.begin(true, true);
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::Idle));
    h.transport.setDefault(HttpMethod::Get, device_paths::kPendingSession, respond(401));
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::AuthFailed, 10000));
    TEST_ASSERT_TRUE(h.feedback.saw(FeedbackSignal::AuthenticationFailed));

    const size_t before = h.transport.requests.size();
    const size_t heartbeatsBefore = h.requests(HttpMethod::Post, device_paths::kHeartbeat);
    h.runFor(h.controllerSettings.authRetryMs - 1000);
    ASSERT_ENUM(ReaderState::AuthFailed, h.controller.state());
    TEST_ASSERT_EQUAL_INT(static_cast<int>(before), static_cast<int>(h.transport.requests.size()));

    // About five minutes later a heartbeat probes the credential (the key was not erased) and polling resumes.
    h.transport.setDefault(HttpMethod::Get, device_paths::kPendingSession, respond(204));
    h.runFor(2000);
    TEST_ASSERT_TRUE(h.requests(HttpMethod::Post, device_paths::kHeartbeat) > heartbeatsBefore);
    ASSERT_ENUM(ReaderState::Idle, h.controller.state());
}

static void test_403_pauses_only_the_missing_scope() {
    Harness h;
    h.controller.begin(true, true);
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::Idle));
    const size_t enrollmentPollsBefore = h.requests(HttpMethod::Get, device_paths::kPendingEnrollment);
    h.transport.setDefault(HttpMethod::Get, device_paths::kPendingEnrollment, respond(403));
    h.runFor(60000);
    const size_t enrollmentPolls = h.requests(HttpMethod::Get, device_paths::kPendingEnrollment) - enrollmentPollsBefore;
    TEST_ASSERT_TRUE(enrollmentPolls <= 1);
    TEST_ASSERT_TRUE(h.requests(HttpMethod::Get, device_paths::kPendingSession) >= 15);
    TEST_ASSERT_TRUE(h.logger.contains("missing_scope"));
    ASSERT_ENUM(ReaderState::Idle, h.controller.state());
}

static void test_429_stops_all_requests_until_retry_after() {
    Harness h;
    h.controller.begin(true, true);
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::Idle));
    h.runFor(5000);
    const std::string route = FakeTransport::key(HttpMethod::Get, device_paths::kPendingSession);
    h.transport.queue(HttpMethod::Get, device_paths::kPendingSession, respond(429, "", 20));
    // Run until the next session poll receives the 429.
    while (!h.transport.queued[route].empty()) {
        h.controller.tick();
        h.clock.advance(10);
    }
    const uint64_t throttledAt = h.clock.now;
    const size_t before = h.transport.requests.size();
    h.runFor(19000);
    TEST_ASSERT_EQUAL_INT(static_cast<int>(before), static_cast<int>(h.transport.requests.size()));
    h.runFor(2000);
    TEST_ASSERT_TRUE(h.transport.requests.size() > before);
    TEST_ASSERT_TRUE(h.clock.now - throttledAt >= 20000);
}

static void test_wifi_loss_in_idle_recovers_without_rescanning() {
    Harness h;
    h.controller.begin(true, true);
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::Idle));
    const int probes = h.sensor.probeCalls;
    h.network.up = false;
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::NetworkRecovery, 1000));
    h.runFor(5000);
    h.network.up = true;
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::Idle, 1000));
    TEST_ASSERT_EQUAL_INT(probes, h.sensor.probeCalls);
}

static void test_boot_with_stored_journal_offline_keeps_slot_then_replays() {
    Harness h;
    EnrollmentJournal journal;
    journal.enrollmentId = uuid(41);
    journal.slot = 7;
    journal.state = JournalState::Stored;
    h.journals.save(journal);
    h.sensor.occupy(7);
    const std::string resultPath = device_paths::enrollmentResult(uuid(41));
    h.transport.setDefault(HttpMethod::Post, resultPath, respond(0));

    h.controller.begin(true, true);
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::Idle));
    // Not an orphan: the journal owns it.
    TEST_ASSERT_TRUE(h.sensor.occupied(7));
    TEST_ASSERT_EQUAL_INT(0, h.sensor.deleteCalls);
    EnrollmentJournal loaded;
    ASSERT_ENUM(JournalLoad::Loaded, h.journals.load(loaded));

    // Back online: the same result is accepted and the server now lists the slot.
    h.transport.setDefault(HttpMethod::Post, resultPath, respond(204));
    h.transport.setDefault(HttpMethod::Get, device_paths::kSlots, respond(200, "{\"slots\":[7]}"));
    h.runFor(60000);
    ASSERT_ENUM(JournalLoad::None, h.journals.load(loaded));
    TEST_ASSERT_TRUE(h.sensor.occupied(7));
    const std::vector<std::string> bodies = h.transport.bodies(HttpMethod::Post, resultPath);
    TEST_ASSERT_TRUE(bodies.size() >= 2);
    for (const std::string& body : bodies) {
        TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"Enrolled\",\"slot\":7}", body.c_str());
    }
    // An interrupted enrollment was resolved: reconciliation ran again afterwards.
    TEST_ASSERT_TRUE(h.requests(HttpMethod::Get, device_paths::kSlots) >= 2);
}

static void test_orphan_from_a_lost_enrollment_is_cleaned_at_boot() {
    Harness h;
    h.sensor.occupy(7);  // stored before a reboot, journal lost with the old controller
    h.controller.begin(true, true);
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::Idle));
    TEST_ASSERT_FALSE(h.sensor.occupied(7));
    TEST_ASSERT_EQUAL_INT(2, static_cast<int>(h.requests(HttpMethod::Get, device_paths::kSlots)));
}

static void test_status_lines_carry_counts_not_slots() {
    Harness h;
    h.sensor.occupy(37);
    h.transport.setDefault(HttpMethod::Get, device_paths::kSlots, respond(200, "{\"slots\":[37]}"));
    h.controller.begin(true, true);
    TEST_ASSERT_TRUE(h.runUntil(ReaderState::Idle));
    std::string status;
    for (const std::string& line : h.controller.statusLines()) {
        status += line + "\n";
    }
    TEST_ASSERT_TRUE(containsText(status, "estado: IDLE"));
    TEST_ASSERT_TRUE(containsText(status, "capacidad del sensor: 50"));
    TEST_ASSERT_TRUE(containsText(status, "slots ocupados (fisicos): 1"));
    TEST_ASSERT_FALSE(containsText(status, "37"));
    TEST_ASSERT_FALSE(h.logger.contains("37"));
}

int main(int, char**) {
    UNITY_BEGIN();
    RUN_TEST(test_unprovisioned_reader_waits_in_provisioning);
    RUN_TEST(test_boot_order_and_no_polling_before_reconciliation);
    RUN_TEST(test_wifi_unavailable_uses_exponential_backoff_up_to_30_seconds);
    RUN_TEST(test_https_waits_for_time_and_development_http_continues);
    RUN_TEST(test_sensor_unavailable_enters_sensor_error_and_recovers);
    RUN_TEST(test_401_enters_auth_failed_and_stops_polling);
    RUN_TEST(test_403_pauses_only_the_missing_scope);
    RUN_TEST(test_429_stops_all_requests_until_retry_after);
    RUN_TEST(test_wifi_loss_in_idle_recovers_without_rescanning);
    RUN_TEST(test_boot_with_stored_journal_offline_keeps_slot_then_replays);
    RUN_TEST(test_orphan_from_a_lost_enrollment_is_cleaned_at_boot);
    RUN_TEST(test_status_lines_carry_counts_not_slots);
    return UNITY_END();
}
