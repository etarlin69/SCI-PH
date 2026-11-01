// Durable records (enrollment journal, deletion attempt), the RAM slot map and the physical slot scan.
#include <unity.h>

#include "../support/fakes.h"
#include "fingerprint/cleanup/deletion_attempt.h"
#include "fingerprint/reconciliation/physical_slot_scanner.h"
#include "storage/journal/enrollment_journal.h"
#include "storage/slot_map/slot_map.h"

using namespace sciph;
using namespace sciph::testing;

void setUp() {}
void tearDown() {}

static void test_journal_round_trip() {
    EnrollmentJournal journal;
    journal.enrollmentId = uuid(7);
    journal.slot = 737;
    journal.state = JournalState::Stored;
    journal.hasExpiry = true;
    journal.expiresAtEpoch = 1793102490LL;
    journal.uncertainDelivery = true;

    EnrollmentJournal parsed;
    TEST_ASSERT_TRUE(parseJournal(serializeJournal(journal), parsed));
    TEST_ASSERT_EQUAL_STRING(journal.enrollmentId.c_str(), parsed.enrollmentId.c_str());
    TEST_ASSERT_EQUAL_INT(737, parsed.slot);
    ASSERT_ENUM(JournalState::Stored, parsed.state);
    TEST_ASSERT_TRUE(parsed.hasExpiry);
    TEST_ASSERT_TRUE(parsed.expiresAtEpoch == 1793102490LL);
    TEST_ASSERT_TRUE(parsed.uncertainDelivery);

    journal.hasExpiry = false;
    journal.state = JournalState::Storing;
    journal.uncertainDelivery = false;
    TEST_ASSERT_TRUE(parseJournal(serializeJournal(journal), parsed));
    TEST_ASSERT_FALSE(parsed.hasExpiry);
    ASSERT_ENUM(JournalState::Storing, parsed.state);
}

static void test_corrupt_journal_is_reported_not_guessed() {
    EnrollmentJournal parsed;
    TEST_ASSERT_FALSE(parseJournal("", parsed));
    TEST_ASSERT_FALSE(parseJournal("2|" + uuid(1) + "|1|2|-|0", parsed));
    TEST_ASSERT_FALSE(parseJournal("1|not-a-uuid|1|2|-|0", parsed));
    TEST_ASSERT_FALSE(parseJournal("1|" + uuid(1) + "|-4|2|-|0", parsed));
    TEST_ASSERT_FALSE(parseJournal("1|" + uuid(1) + "|4|9|-|0", parsed));
    TEST_ASSERT_FALSE(parseJournal("1|" + uuid(1) + "|4|2|-", parsed));

    MemoryStore store;
    EnrollmentJournalStore journals(store);
    EnrollmentJournal loaded;
    ASSERT_ENUM(JournalLoad::None, journals.load(loaded));
    store.put("sciph-jrnl", "enrollment", "garbage");
    ASSERT_ENUM(JournalLoad::Unreadable, journals.load(loaded));
}

static void test_journal_store_is_one_atomic_value() {
    MemoryStore store;
    EnrollmentJournalStore journals(store);
    EnrollmentJournal journal;
    journal.enrollmentId = uuid(3);
    journal.slot = 4;
    TEST_ASSERT_TRUE(journals.save(journal));
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(store.values.size()));
    EnrollmentJournal loaded;
    ASSERT_ENUM(JournalLoad::Loaded, journals.load(loaded));
    TEST_ASSERT_TRUE(journals.clear());
    ASSERT_ENUM(JournalLoad::None, journals.load(loaded));
}

static void test_deletion_attempt_round_trip() {
    DeletionAttempt attempt;
    attempt.deletionId = uuid(11);
    attempt.attemptId = uuid(12);
    attempt.outcome = DeletionOutcome::NotFound;
    DeletionAttempt parsed;
    TEST_ASSERT_TRUE(parseDeletionAttempt(serializeDeletionAttempt(attempt), parsed));
    TEST_ASSERT_EQUAL_STRING(uuid(12).c_str(), parsed.attemptId.c_str());
    ASSERT_ENUM(DeletionOutcome::NotFound, parsed.outcome);
    TEST_ASSERT_FALSE(parseDeletionAttempt("1|" + uuid(11) + "|" + uuid(12) + "|Maybe", parsed));

    MemoryStore store;
    DeletionAttemptStore attempts(store);
    TEST_ASSERT_FALSE(attempts.load(parsed));
    TEST_ASSERT_TRUE(attempts.save(attempt));
    TEST_ASSERT_TRUE(attempts.load(parsed));
    TEST_ASSERT_TRUE(attempts.clear());
    TEST_ASSERT_FALSE(attempts.load(parsed));
}

static void test_slot_map_states_and_capacity_boundaries() {
    SlotMap map;
    map.reset(10);
    TEST_ASSERT_EQUAL_INT(10, map.capacity());
    TEST_ASSERT_FALSE(map.scanned());
    TEST_ASSERT_TRUE(map.inRange(0));
    TEST_ASSERT_TRUE(map.inRange(9));
    TEST_ASSERT_FALSE(map.inRange(10));
    TEST_ASSERT_FALSE(map.inRange(-1));
    ASSERT_ENUM(SlotState::Unknown, map.get(3));
    map.set(3, SlotState::Occupied);
    map.set(10, SlotState::Occupied);  // ignored
    map.set(-1, SlotState::Occupied);  // ignored
    ASSERT_ENUM(SlotState::Occupied, map.get(3));
    ASSERT_ENUM(SlotState::Unknown, map.get(10));
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(map.count(SlotState::Occupied)));
    TEST_ASSERT_EQUAL_INT(9, static_cast<int>(map.count(SlotState::Unknown)));
    map.markScanned();
    map.reset(12);
    TEST_ASSERT_FALSE(map.scanned());
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(map.count(SlotState::Occupied)));
}

static void test_server_snapshot_is_sorted_and_unique() {
    ServerSlotSnapshot snapshot;
    TEST_ASSERT_FALSE(snapshot.known());
    snapshot.update({7, 1, 7, 3});
    TEST_ASSERT_TRUE(snapshot.known());
    TEST_ASSERT_EQUAL_INT(3, static_cast<int>(snapshot.size()));
    TEST_ASSERT_TRUE(snapshot.contains(7));
    TEST_ASSERT_FALSE(snapshot.contains(2));
}

static void test_scan_falls_back_to_loadchar_when_index_table_is_unsupported() {
    FakeSensor sensor(40);
    sensor.occupy(0);
    sensor.occupy(13);
    sensor.occupy(39);
    SlotMap map;
    map.reset(40);
    PhysicalSlotScanner scanner(sensor);
    const ScanReport report = scanner.scan(map);
    TEST_ASSERT_TRUE(report.completed);
    TEST_ASSERT_FALSE(report.usedIndexTable);
    TEST_ASSERT_EQUAL_INT(3, static_cast<int>(report.occupied));
    TEST_ASSERT_TRUE(map.scanned());
    ASSERT_ENUM(SlotState::Occupied, map.get(13));
    ASSERT_ENUM(SlotState::Empty, map.get(14));
    TEST_ASSERT_EQUAL_INT(40, sensor.probeCalls);
}

static void test_scan_uses_a_verified_index_table() {
    FakeSensor sensor(600);
    sensor.indexTableSupported = true;
    sensor.occupy(1);
    sensor.occupy(300);
    sensor.occupy(599);
    SlotMap map;
    map.reset(600);
    PhysicalSlotScanner scanner(sensor);
    const ScanReport report = scanner.scan(map);
    TEST_ASSERT_TRUE(report.completed);
    TEST_ASSERT_TRUE(report.usedIndexTable);
    ASSERT_ENUM(SlotState::Occupied, map.get(300));
    ASSERT_ENUM(SlotState::Occupied, map.get(599));
    ASSERT_ENUM(SlotState::Empty, map.get(2));
    TEST_ASSERT_TRUE(sensor.probeCalls < 20);
}

static void test_scan_rejects_an_index_table_that_disagrees_with_loadchar() {
    FakeSensor sensor(64);
    sensor.indexTableSupported = true;
    sensor.indexTableLsbFirst = false;  // a clone with another bit order
    sensor.occupy(1);
    SlotMap map;
    map.reset(64);
    PhysicalSlotScanner scanner(sensor);
    const ScanReport report = scanner.scan(map);
    TEST_ASSERT_TRUE(report.completed);
    TEST_ASSERT_FALSE(report.usedIndexTable);
    ASSERT_ENUM(SlotState::Occupied, map.get(1));
    ASSERT_ENUM(SlotState::Empty, map.get(6));
}

static void test_scan_marks_unclear_answers_unknown_and_aborts_on_a_dead_sensor() {
    FakeSensor sensor(20);
    sensor.probeOverrides[5] = SensorStatus::Failed;
    SlotMap map;
    map.reset(20);
    PhysicalSlotScanner scanner(sensor);
    ScanReport report = scanner.scan(map);
    TEST_ASSERT_TRUE(report.completed);
    ASSERT_ENUM(SlotState::Unknown, map.get(5));
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(report.unknown));

    FakeSensor dead(20);
    dead.responding = false;
    SlotMap deadMap;
    deadMap.reset(20);
    PhysicalSlotScanner deadScanner(dead);
    report = deadScanner.scan(deadMap);
    TEST_ASSERT_FALSE(report.completed);
    TEST_ASSERT_FALSE(deadMap.scanned());
}

int main(int, char**) {
    UNITY_BEGIN();
    RUN_TEST(test_journal_round_trip);
    RUN_TEST(test_corrupt_journal_is_reported_not_guessed);
    RUN_TEST(test_journal_store_is_one_atomic_value);
    RUN_TEST(test_deletion_attempt_round_trip);
    RUN_TEST(test_slot_map_states_and_capacity_boundaries);
    RUN_TEST(test_server_snapshot_is_sorted_and_unique);
    RUN_TEST(test_scan_falls_back_to_loadchar_when_index_table_is_unsupported);
    RUN_TEST(test_scan_uses_a_verified_index_table);
    RUN_TEST(test_scan_rejects_an_index_table_that_disagrees_with_loadchar);
    RUN_TEST(test_scan_marks_unclear_answers_unknown_and_aborts_on_a_dead_sensor);
    return UNITY_END();
}
