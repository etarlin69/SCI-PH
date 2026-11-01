// Enrollment: two captures, journal before Store, probe before Store, result handling, compensation, boot recovery
// and the same-result replay. Slot 737 is used so a leaked slot number is easy to spot in the logs.
#include <unity.h>

#include "../support/fakes.h"
#include "api/device_api_client.h"
#include "fingerprint/enrollment/enrollment_coordinator.h"
#include "fingerprint/reconciliation/physical_slot_scanner.h"
#include "storage/journal/enrollment_journal.h"
#include "storage/slot_map/slot_map.h"

using namespace sciph;
using namespace sciph::testing;

void setUp() {}
void tearDown() {}

namespace {

const std::string kEnrollmentId = uuid(1);
const int kSlot = 737;

struct Harness {
    FakeClock clock;
    FakeTransport transport;
    FakeSensor sensor{800};
    MemoryStore store;
    RecordingFeedback feedback;
    CapturingLogger logger;
    DeviceApiClient api{transport, clock};
    SlotMap slots;
    ServerSlotSnapshot server;
    EnrollmentJournalStore journals{store};
    CaptureSettings capture;
    ReplaySettings replay;
    EnrollmentCoordinator coordinator{api, sensor, slots, server, journals, feedback, clock, logger, capture, replay};

    // Slots 0..kSlot-1 physically occupied, kSlot is the first empty one.
    Harness() {
        for (int slot = 0; slot < kSlot; ++slot) {
            sensor.occupy(slot);
        }
    }

    void scan() {
        slots.reset(800);
        PhysicalSlotScanner scanner(sensor);
        scanner.scan(slots);
    }

    void pendingEnrollment() {
        transport.queue(HttpMethod::Get, device_paths::kPendingEnrollment,
                        respond(200, "{\"enrollmentId\":\"" + kEnrollmentId + "\",\"expiresAtUtc\":\"2027-01-01T00:02:00Z\"}"));
    }

    void fingerTwice() { sensor.script({SensorStatus::Ok, SensorStatus::NoFinger, SensorStatus::Ok}); }

    std::string resultPath() const { return device_paths::enrollmentResult(kEnrollmentId); }

    void answerResult(int status) { transport.queue(HttpMethod::Post, resultPath(), respond(status)); }

    std::vector<std::string> resultBodies() const { return transport.bodies(HttpMethod::Post, resultPath()); }

    JournalLoad journal(EnrollmentJournal& journal) { return journals.load(journal); }

    void seedJournal(JournalState state, bool uncertain = false) {
        EnrollmentJournal journal;
        journal.enrollmentId = kEnrollmentId;
        journal.slot = kSlot;
        journal.state = state;
        journal.uncertainDelivery = uncertain;
        journals.save(journal);
    }
};

const std::string kEnrolledBody = "{\"outcome\":\"Enrolled\",\"slot\":737}";

void assertNoSlotInLogs(const Harness& h) { TEST_ASSERT_FALSE(h.logger.contains("737")); }

}  // namespace

static void test_happy_path_stores_after_journal_and_reports_enrolled() {
    Harness h;
    h.scan();
    h.pendingEnrollment();
    h.fingerTwice();
    h.answerResult(204);
    bool journaledBeforeStore = false;
    h.sensor.beforeStore = [&](int slot) {
        EnrollmentJournal journal;
        journaledBeforeStore = h.journals.load(journal) == JournalLoad::Loaded && journal.slot == slot &&
                               journal.state == JournalState::Storing && journal.enrollmentId == kEnrollmentId;
    };

    const OperationReport report = h.coordinator.poll();

    TEST_ASSERT_TRUE(journaledBeforeStore);
    TEST_ASSERT_EQUAL_INT(1, h.sensor.storeCalls);
    TEST_ASSERT_TRUE(h.sensor.occupied(kSlot));
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(h.resultBodies().size()));
    TEST_ASSERT_EQUAL_STRING(kEnrolledBody.c_str(), h.resultBodies()[0].c_str());
    ASSERT_ENUM(ApiStatus::NoContent, report.api.status);
    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::None, h.journal(journal));
    TEST_ASSERT_FALSE(h.coordinator.busy());
    ASSERT_ENUM(SlotState::Occupied, h.slots.get(kSlot));
    TEST_ASSERT_TRUE(h.feedback.saw(FeedbackSignal::PlaceFinger));
    TEST_ASSERT_TRUE(h.feedback.saw(FeedbackSignal::RemoveFinger));
    TEST_ASSERT_TRUE(h.feedback.saw(FeedbackSignal::PlaceAgain));
    TEST_ASSERT_TRUE(h.feedback.saw(FeedbackSignal::Success));
    assertNoSlotInLogs(h);
}

static void test_slot_known_to_the_server_is_never_chosen() {
    Harness h;
    h.server.update({kSlot});  // physically empty, but a pending deletion or an active reference for SCI-PH
    h.scan();
    h.pendingEnrollment();
    h.fingerTwice();
    h.answerResult(204);
    h.coordinator.poll();
    TEST_ASSERT_FALSE(h.sensor.occupied(kSlot));
    TEST_ASSERT_TRUE(h.sensor.occupied(kSlot + 1));
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"Enrolled\",\"slot\":738}", h.resultBodies()[0].c_str());
}

static void test_uncertain_answer_keeps_slot_and_replays_the_same_result() {
    Harness h;
    h.scan();
    h.pendingEnrollment();
    h.fingerTwice();
    h.answerResult(503);
    h.coordinator.poll();

    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::Loaded, h.journal(journal));
    ASSERT_ENUM(JournalState::Stored, journal.state);
    TEST_ASSERT_TRUE(journal.uncertainDelivery);
    TEST_ASSERT_TRUE(h.sensor.occupied(kSlot));
    TEST_ASSERT_TRUE(h.coordinator.busy());
    TEST_ASSERT_FALSE(h.coordinator.replayDue(h.clock.now));

    h.clock.advance(h.replay.initialDelayMs);
    TEST_ASSERT_TRUE(h.coordinator.replayDue(h.clock.now));
    h.transport.queue(HttpMethod::Post, h.resultPath(), respond(0));  // timeout
    h.coordinator.replay();
    h.clock.advance(h.replay.maxDelayMs);
    h.answerResult(204);
    const OperationReport report = h.coordinator.replay();

    const std::vector<std::string> bodies = h.resultBodies();
    TEST_ASSERT_EQUAL_INT(3, static_cast<int>(bodies.size()));
    for (const std::string& body : bodies) {
        TEST_ASSERT_EQUAL_STRING(kEnrolledBody.c_str(), body.c_str());
    }
    TEST_ASSERT_TRUE(report.journalResolved);
    ASSERT_ENUM(JournalLoad::None, h.journal(journal));
    TEST_ASSERT_TRUE(h.sensor.occupied(kSlot));
    TEST_ASSERT_EQUAL_INT(0, h.sensor.deleteCalls);
    assertNoSlotInLogs(h);
}

static void assertCompensatedAfter(int status) {
    Harness h;
    h.scan();
    h.pendingEnrollment();
    h.fingerTwice();
    h.answerResult(status);
    h.coordinator.poll();
    TEST_ASSERT_FALSE(h.sensor.occupied(kSlot));
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(h.sensor.deleted.size()));
    TEST_ASSERT_EQUAL_INT(kSlot, h.sensor.deleted[0]);
    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::None, h.journal(journal));
    ASSERT_ENUM(SlotState::Empty, h.slots.get(kSlot));
    TEST_ASSERT_FALSE(h.coordinator.busy());
    assertNoSlotInLogs(h);
}

static void test_409_deletes_the_new_template() { assertCompensatedAfter(409); }
static void test_404_deletes_the_new_template() { assertCompensatedAfter(404); }
static void test_400_deletes_the_new_template() { assertCompensatedAfter(400); }
static void test_403_before_any_uncertain_delivery_deletes_the_new_template() { assertCompensatedAfter(403); }

static void test_403_after_an_uncertain_delivery_keeps_the_slot() {
    Harness h;
    h.scan();
    h.pendingEnrollment();
    h.fingerTwice();
    h.answerResult(0);
    h.coordinator.poll();
    h.clock.advance(h.replay.maxDelayMs);
    h.answerResult(403);
    const OperationReport report = h.coordinator.replay();
    ASSERT_ENUM(ApiStatus::Forbidden, report.api.status);
    TEST_ASSERT_TRUE(h.sensor.occupied(kSlot));
    TEST_ASSERT_EQUAL_INT(0, h.sensor.deleteCalls);
    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::Loaded, h.journal(journal));
    ASSERT_ENUM(JournalState::Stored, journal.state);
}

static void test_401_keeps_the_slot_and_the_journal() {
    Harness h;
    h.scan();
    h.pendingEnrollment();
    h.fingerTwice();
    h.answerResult(401);
    const OperationReport report = h.coordinator.poll();
    ASSERT_ENUM(ApiStatus::Unauthorized, report.api.status);
    TEST_ASSERT_TRUE(h.sensor.occupied(kSlot));
    TEST_ASSERT_EQUAL_INT(0, h.sensor.deleteCalls);
    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::Loaded, h.journal(journal));
}

static void test_mismatched_captures_report_failed_without_store() {
    Harness h;
    h.scan();
    h.pendingEnrollment();
    h.fingerTwice();
    h.sensor.createModelResult = SensorStatus::Mismatch;
    h.coordinator.poll();
    TEST_ASSERT_EQUAL_INT(0, h.sensor.storeCalls);
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"Failed\"}", h.resultBodies()[0].c_str());
    TEST_ASSERT_TRUE(h.feedback.saw(FeedbackSignal::FingersDoNotMatch));
    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::None, h.journal(journal));
}

static void test_low_quality_is_retried_then_reported() {
    Harness h;
    h.scan();
    h.pendingEnrollment();
    h.sensor.script({SensorStatus::LowQuality, SensorStatus::NoFinger, SensorStatus::LowQuality, SensorStatus::NoFinger,
                     SensorStatus::LowQuality});
    h.coordinator.poll();
    TEST_ASSERT_EQUAL_INT(0, h.sensor.storeCalls);
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"LowQuality\"}", h.resultBodies()[0].c_str());
}

static void test_capture_timeout_sends_nothing() {
    Harness h;
    h.scan();
    h.pendingEnrollment();
    const OperationReport report = h.coordinator.poll();
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(h.resultBodies().size()));
    TEST_ASSERT_EQUAL_INT(0, h.sensor.storeCalls);
    TEST_ASSERT_FALSE(h.coordinator.busy());
    TEST_ASSERT_FALSE(report.sensorFault);
}

static void test_slot_taken_right_before_store_moves_to_another_empty_slot() {
    Harness h;
    h.scan();
    h.pendingEnrollment();
    h.fingerTwice();
    h.answerResult(204);
    int probesOfSlot = 0;
    h.sensor.beforeProbe = [&](int slot) {
        if (slot == kSlot && ++probesOfSlot == 2) {
            h.sensor.occupy(kSlot);  // something wrote it between the choice and the Store
        }
    };
    bool journaledNewSlot = false;
    h.sensor.beforeStore = [&](int slot) {
        EnrollmentJournal journal;
        journaledNewSlot = h.journals.load(journal) == JournalLoad::Loaded && journal.slot == slot && slot == kSlot + 1;
    };
    h.coordinator.poll();
    TEST_ASSERT_TRUE(journaledNewSlot);
    TEST_ASSERT_EQUAL_INT(kSlot + 1, h.sensor.stored[0]);
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"Enrolled\",\"slot\":738}", h.resultBodies()[0].c_str());
}

static void test_full_sensor_reports_failed() {
    Harness h;
    for (int slot = 0; slot < 800; ++slot) {
        h.sensor.occupy(slot);
    }
    h.scan();
    h.pendingEnrollment();
    h.coordinator.poll();
    TEST_ASSERT_EQUAL_INT(0, h.sensor.storeCalls);
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"Failed\"}", h.resultBodies()[0].c_str());
    TEST_ASSERT_TRUE(h.feedback.saw(FeedbackSignal::SensorFull));
}

static void test_journal_write_failure_prevents_store() {
    Harness h;
    h.scan();
    h.pendingEnrollment();
    h.fingerTwice();
    h.store.failPuts = true;
    h.coordinator.poll();
    TEST_ASSERT_EQUAL_INT(0, h.sensor.storeCalls);
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"SensorError\"}", h.resultBodies()[0].c_str());
}

static void test_store_failure_is_compensated_and_reported() {
    Harness h;
    h.scan();
    h.pendingEnrollment();
    h.fingerTwice();
    h.sensor.storeResult = SensorStatus::Failed;
    const OperationReport report = h.coordinator.poll();
    TEST_ASSERT_TRUE(report.sensorFault);
    TEST_ASSERT_FALSE(h.sensor.occupied(kSlot));
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"SensorError\"}", h.resultBodies()[0].c_str());
    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::None, h.journal(journal));
}

static void test_boot_storing_with_empty_slot_clears_the_journal() {
    Harness h;
    h.seedJournal(JournalState::Storing);
    h.scan();
    const OperationReport report = h.coordinator.recover();
    TEST_ASSERT_TRUE(report.journalResolved);
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(h.resultBodies().size()));
    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::None, h.journal(journal));
}

static void test_boot_storing_with_occupied_slot_is_treated_as_stored() {
    Harness h;
    h.seedJournal(JournalState::Storing);
    h.sensor.occupy(kSlot);
    h.scan();
    h.answerResult(204);
    h.coordinator.recover();
    TEST_ASSERT_EQUAL_STRING(kEnrolledBody.c_str(), h.resultBodies()[0].c_str());
    TEST_ASSERT_TRUE(h.sensor.occupied(kSlot));
    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::None, h.journal(journal));
}

static void test_boot_stored_rejected_with_409_deletes_then_clears() {
    Harness h;
    h.seedJournal(JournalState::Stored);
    h.sensor.occupy(kSlot);
    h.scan();
    h.answerResult(409);
    h.coordinator.recover();
    TEST_ASSERT_FALSE(h.sensor.occupied(kSlot));
    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::None, h.journal(journal));
}

static void test_boot_stored_while_offline_keeps_everything() {
    Harness h;
    h.seedJournal(JournalState::Stored);
    h.sensor.occupy(kSlot);
    h.scan();
    h.transport.offline = true;
    h.coordinator.recover();
    TEST_ASSERT_TRUE(h.sensor.occupied(kSlot));
    TEST_ASSERT_EQUAL_INT(0, h.sensor.deleteCalls);
    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::Loaded, h.journal(journal));
    TEST_ASSERT_TRUE(journal.uncertainDelivery);
    const JournalGuard guard = h.coordinator.journalGuard();
    TEST_ASSERT_TRUE(guard.hasSlot);
    TEST_ASSERT_EQUAL_INT(kSlot, guard.slot);

    // Back online: the same result, accepted.
    h.transport.offline = false;
    h.clock.advance(h.replay.maxDelayMs);
    h.answerResult(204);
    h.coordinator.replay();
    TEST_ASSERT_TRUE(h.sensor.occupied(kSlot));
    ASSERT_ENUM(JournalLoad::None, h.journal(journal));
    const std::vector<std::string> bodies = h.resultBodies();
    TEST_ASSERT_EQUAL_STRING(bodies[0].c_str(), bodies[1].c_str());
}

static void test_boot_compensating_deletes_then_clears() {
    Harness h;
    h.seedJournal(JournalState::Compensating);
    h.sensor.occupy(kSlot);
    h.scan();
    h.coordinator.recover();
    TEST_ASSERT_FALSE(h.sensor.occupied(kSlot));
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(h.resultBodies().size()));
    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::None, h.journal(journal));
}

static void test_compensation_failure_keeps_the_journal() {
    Harness h;
    h.seedJournal(JournalState::Compensating);
    h.sensor.occupy(kSlot);
    h.sensor.deleteResult = SensorStatus::Failed;
    h.scan();
    const OperationReport report = h.coordinator.recover();
    TEST_ASSERT_TRUE(report.sensorFault);
    EnrollmentJournal journal;
    ASSERT_ENUM(JournalLoad::Loaded, h.journal(journal));
    ASSERT_ENUM(JournalState::Compensating, journal.state);
}

static void test_unreadable_journal_blocks_enrollment_until_discarded() {
    Harness h;
    h.store.put("sciph-jrnl", "enrollment", "corrupt");
    h.scan();
    h.coordinator.recover();
    TEST_ASSERT_TRUE(h.coordinator.busy());
    TEST_ASSERT_TRUE(h.coordinator.journalGuard().unreadable);
    h.pendingEnrollment();
    h.coordinator.poll();
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(h.transport.requests.size()));
    TEST_ASSERT_TRUE(h.coordinator.discardUnreadableJournal());
    TEST_ASSERT_FALSE(h.coordinator.busy());
}

static void test_failure_result_is_replayed_then_abandoned() {
    Harness h;
    h.scan();
    h.pendingEnrollment();
    h.fingerTwice();
    h.sensor.createModelResult = SensorStatus::Mismatch;
    h.transport.setDefault(HttpMethod::Post, h.resultPath(), respond(503));
    h.coordinator.poll();
    TEST_ASSERT_TRUE(h.coordinator.busy());
    h.clock.advance(h.replay.maxDelayMs);
    h.coordinator.replay();
    const std::vector<std::string> bodies = h.resultBodies();
    TEST_ASSERT_EQUAL_INT(2, static_cast<int>(bodies.size()));
    TEST_ASSERT_EQUAL_STRING(bodies[0].c_str(), bodies[1].c_str());
    h.clock.advance(h.replay.abandonAfterMs + 1);
    h.coordinator.replay();
    TEST_ASSERT_FALSE(h.coordinator.busy());
}

int main(int, char**) {
    UNITY_BEGIN();
    RUN_TEST(test_happy_path_stores_after_journal_and_reports_enrolled);
    RUN_TEST(test_slot_known_to_the_server_is_never_chosen);
    RUN_TEST(test_uncertain_answer_keeps_slot_and_replays_the_same_result);
    RUN_TEST(test_409_deletes_the_new_template);
    RUN_TEST(test_404_deletes_the_new_template);
    RUN_TEST(test_400_deletes_the_new_template);
    RUN_TEST(test_403_before_any_uncertain_delivery_deletes_the_new_template);
    RUN_TEST(test_403_after_an_uncertain_delivery_keeps_the_slot);
    RUN_TEST(test_401_keeps_the_slot_and_the_journal);
    RUN_TEST(test_mismatched_captures_report_failed_without_store);
    RUN_TEST(test_low_quality_is_retried_then_reported);
    RUN_TEST(test_capture_timeout_sends_nothing);
    RUN_TEST(test_slot_taken_right_before_store_moves_to_another_empty_slot);
    RUN_TEST(test_full_sensor_reports_failed);
    RUN_TEST(test_journal_write_failure_prevents_store);
    RUN_TEST(test_store_failure_is_compensated_and_reported);
    RUN_TEST(test_boot_storing_with_empty_slot_clears_the_journal);
    RUN_TEST(test_boot_storing_with_occupied_slot_is_treated_as_stored);
    RUN_TEST(test_boot_stored_rejected_with_409_deletes_then_clears);
    RUN_TEST(test_boot_stored_while_offline_keeps_everything);
    RUN_TEST(test_boot_compensating_deletes_then_clears);
    RUN_TEST(test_compensation_failure_keeps_the_journal);
    RUN_TEST(test_unreadable_journal_blocks_enrollment_until_discarded);
    RUN_TEST(test_failure_result_is_replayed_then_abandoned);
    return UNITY_END();
}
