// Physical deletion of revoked slots: Deleted / NotFound / SensorError, attemptId persistence and replay, a new
// attemptId after an accepted SensorError, terminal answers and deferral behind an enrollment journal.
#include <unity.h>

#include "../support/fakes.h"
#include "api/device_api_client.h"
#include "fingerprint/cleanup/deletion_attempt.h"
#include "fingerprint/cleanup/deletion_coordinator.h"
#include "storage/slot_map/slot_map.h"

using namespace sciph;
using namespace sciph::testing;

void setUp() {}
void tearDown() {}

namespace {

const std::string kDeletionId = uuid(31);
const uint32_t kSensorErrorRetryMs = 30000;

struct Harness {
    FakeClock clock;
    FakeTransport transport;
    FakeSensor sensor{1000};
    MemoryStore store;
    SequenceRandom random;
    CapturingLogger logger;
    DeviceApiClient api{transport, clock};
    SlotMap slots;
    DeletionAttemptStore attempts{store};
    ReplaySettings replay;
    DeletionCoordinator coordinator{api, sensor, slots, attempts, random, clock, logger, replay, kSensorErrorRetryMs};

    Harness() { slots.reset(1000); }

    void pendingDeletion(int slot) {
        transport.queue(HttpMethod::Get, device_paths::kPendingDeletion,
                        respond(200, "{\"deletionId\":\"" + kDeletionId + "\",\"slot\":" + std::to_string(slot) + "}"));
    }
    std::string resultPath() const { return device_paths::deletionResult(kDeletionId); }
    std::vector<std::string> bodies() const { return transport.bodies(HttpMethod::Post, resultPath()); }
};

std::string attemptIdOf(const std::string& body) {
    const std::string marker = "\"attemptId\":\"";
    const size_t start = body.find(marker) + marker.size();
    return body.substr(start, 36);
}

}  // namespace

static void test_occupied_slot_is_deleted() {
    Harness h;
    h.sensor.occupy(737);
    h.pendingDeletion(737);
    h.coordinator.poll(JournalGuard());
    TEST_ASSERT_FALSE(h.sensor.occupied(737));
    const std::vector<std::string> bodies = h.bodies();
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(bodies.size()));
    TEST_ASSERT_TRUE(containsText(bodies[0], "\"outcome\":\"Deleted\""));
    TEST_ASSERT_TRUE(isUuid(attemptIdOf(bodies[0])));
    TEST_ASSERT_FALSE(containsText(bodies[0], "slot"));
    TEST_ASSERT_FALSE(h.coordinator.busy());
    DeletionAttempt stored;
    TEST_ASSERT_FALSE(h.attempts.load(stored));
    TEST_ASSERT_FALSE(h.logger.contains("737"));
}

static void test_empty_slot_reports_not_found() {
    Harness h;
    h.pendingDeletion(737);
    h.coordinator.poll(JournalGuard());
    TEST_ASSERT_EQUAL_INT(0, h.sensor.deleteCalls);
    TEST_ASSERT_TRUE(containsText(h.bodies()[0], "\"outcome\":\"NotFound\""));
}

static void test_slot_beyond_capacity_reports_not_found() {
    Harness h;
    h.pendingDeletion(5000);
    h.coordinator.poll(JournalGuard());
    TEST_ASSERT_EQUAL_INT(0, h.sensor.probeCalls);
    TEST_ASSERT_TRUE(containsText(h.bodies()[0], "\"outcome\":\"NotFound\""));
}

static void test_sensor_failure_reports_sensor_error() {
    Harness h;
    h.sensor.occupy(737);
    h.sensor.deleteResult = SensorStatus::CommunicationError;
    h.pendingDeletion(737);
    const OperationReport report = h.coordinator.poll(JournalGuard());
    TEST_ASSERT_TRUE(report.sensorFault);
    TEST_ASSERT_TRUE(containsText(h.bodies()[0], "\"outcome\":\"SensorError\""));
}

static void test_uncertain_answer_replays_the_same_attempt_even_after_reboot() {
    Harness h;
    h.sensor.occupy(737);
    h.pendingDeletion(737);
    h.transport.queue(HttpMethod::Post, h.resultPath(), respond(502));
    h.coordinator.poll(JournalGuard());
    TEST_ASSERT_TRUE(h.coordinator.busy());
    DeletionAttempt persisted;
    TEST_ASSERT_TRUE(h.attempts.load(persisted));

    // Reboot: a new coordinator over the same NVS resends the SAME attemptId and outcome.
    DeletionCoordinator rebooted(h.api, h.sensor, h.slots, h.attempts, h.random, h.clock, h.logger, h.replay,
                                 kSensorErrorRetryMs);
    h.transport.queue(HttpMethod::Post, h.resultPath(), respond(0));
    rebooted.recover();
    h.clock.advance(h.replay.maxDelayMs);
    TEST_ASSERT_TRUE(rebooted.replayDue(h.clock.now));
    rebooted.replay();

    const std::vector<std::string> bodies = h.bodies();
    TEST_ASSERT_EQUAL_INT(3, static_cast<int>(bodies.size()));
    TEST_ASSERT_EQUAL_STRING(bodies[0].c_str(), bodies[1].c_str());
    TEST_ASSERT_EQUAL_STRING(bodies[0].c_str(), bodies[2].c_str());
    TEST_ASSERT_EQUAL_STRING(persisted.attemptId.c_str(), attemptIdOf(bodies[2]).c_str());
    TEST_ASSERT_FALSE(rebooted.busy());
    TEST_ASSERT_FALSE(h.attempts.load(persisted));
    // Only one physical deletion.
    TEST_ASSERT_EQUAL_INT(1, h.sensor.deleteCalls);
}

static void test_new_attempt_id_after_an_accepted_sensor_error() {
    Harness h;
    h.sensor.occupy(737);
    h.sensor.deleteResult = SensorStatus::Failed;
    h.pendingDeletion(737);
    h.coordinator.poll(JournalGuard());
    TEST_ASSERT_FALSE(h.coordinator.busy());

    // The next physical attempt waits, then uses another attemptId.
    h.pendingDeletion(737);
    h.coordinator.poll(JournalGuard());
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(h.bodies().size()));
    h.clock.advance(kSensorErrorRetryMs);
    h.sensor.deleteResult = SensorStatus::Ok;
    h.coordinator.poll(JournalGuard());
    const std::vector<std::string> bodies = h.bodies();
    TEST_ASSERT_EQUAL_INT(2, static_cast<int>(bodies.size()));
    TEST_ASSERT_TRUE(containsText(bodies[1], "\"outcome\":\"Deleted\""));
    TEST_ASSERT_TRUE(attemptIdOf(bodies[0]) != attemptIdOf(bodies[1]));
}

static void test_terminal_answers_are_not_retried() {
    const int statuses[] = {400, 404, 409};
    for (int status : statuses) {
        Harness h;
        h.pendingDeletion(737);
        h.transport.queue(HttpMethod::Post, h.resultPath(), respond(status));
        h.coordinator.poll(JournalGuard());
        TEST_ASSERT_FALSE(h.coordinator.busy());
        DeletionAttempt stored;
        TEST_ASSERT_FALSE(h.attempts.load(stored));
    }
}

static void test_401_and_403_keep_the_attempt() {
    const int statuses[] = {401, 403};
    for (int status : statuses) {
        Harness h;
        h.pendingDeletion(737);
        h.transport.queue(HttpMethod::Post, h.resultPath(), respond(status));
        h.coordinator.poll(JournalGuard());
        TEST_ASSERT_TRUE(h.coordinator.busy());
        DeletionAttempt stored;
        TEST_ASSERT_TRUE(h.attempts.load(stored));
    }
}

static void test_deletion_waits_for_an_enrollment_journal_on_the_same_slot() {
    Harness h;
    h.sensor.occupy(737);
    h.pendingDeletion(737);
    JournalGuard journal;
    journal.hasSlot = true;
    journal.slot = 737;
    h.coordinator.poll(journal);
    TEST_ASSERT_TRUE(h.sensor.occupied(737));
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(h.bodies().size()));

    JournalGuard unreadable;
    unreadable.unreadable = true;
    h.pendingDeletion(737);
    h.coordinator.poll(unreadable);
    TEST_ASSERT_TRUE(h.sensor.occupied(737));
}

int main(int, char**) {
    UNITY_BEGIN();
    RUN_TEST(test_occupied_slot_is_deleted);
    RUN_TEST(test_empty_slot_reports_not_found);
    RUN_TEST(test_slot_beyond_capacity_reports_not_found);
    RUN_TEST(test_sensor_failure_reports_sensor_error);
    RUN_TEST(test_uncertain_answer_replays_the_same_attempt_even_after_reboot);
    RUN_TEST(test_new_attempt_id_after_an_accepted_sensor_error);
    RUN_TEST(test_terminal_answers_are_not_retried);
    RUN_TEST(test_401_and_403_keep_the_attempt);
    RUN_TEST(test_deletion_waits_for_an_enrollment_journal_on_the_same_slot);
    return UNITY_END();
}
