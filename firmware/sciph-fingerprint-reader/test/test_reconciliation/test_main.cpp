// Orphan slot reconciliation: candidates, the second inventory before deleting, and every fail-safe rule.
#include <unity.h>

#include "../support/fakes.h"
#include "api/device_api_client.h"
#include "fingerprint/enrollment/enrollment_coordinator.h"
#include "fingerprint/reconciliation/physical_slot_scanner.h"
#include "fingerprint/reconciliation/slot_reconciler.h"
#include "storage/slot_map/slot_map.h"

using namespace sciph;
using namespace sciph::testing;

void setUp() {}
void tearDown() {}

namespace {

struct Harness {
    FakeClock clock;
    FakeTransport transport;
    FakeSensor sensor{1000};
    CapturingLogger logger;
    DeviceApiClient api{transport, clock};
    SlotMap slots;
    ServerSlotSnapshot server;
    ReconciliationSettings settings;
    SlotReconciler reconciler{api, sensor, slots, server, logger, settings};

    void scan() {
        slots.reset(1000);
        PhysicalSlotScanner scanner(sensor);
        scanner.scan(slots);
    }
    void inventory(const std::string& body) {
        transport.queue(HttpMethod::Get, device_paths::kSlots, respond(200, body));
    }
    size_t inventoryCalls() const { return transport.count(HttpMethod::Get, device_paths::kSlots); }
};

}  // namespace

static void test_orphan_candidates_exclude_server_and_journal_slots() {
    SlotMap slots;
    slots.reset(10);
    for (int slot = 0; slot < 10; ++slot) {
        slots.set(slot, SlotState::Empty);
    }
    slots.set(1, SlotState::Occupied);  // active reference
    slots.set(2, SlotState::Occupied);  // pending deletion (in the inventory too)
    slots.set(3, SlotState::Occupied);  // the journal's slot
    slots.set(4, SlotState::Occupied);  // orphan
    slots.set(5, SlotState::Unknown);   // unreadable: never a candidate
    ServerSlotSnapshot server;
    server.update({1, 2, 8});
    JournalGuard journal;
    journal.hasSlot = true;
    journal.slot = 3;
    const std::vector<int> candidates = orphanCandidates(slots, server, journal);
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(candidates.size()));
    TEST_ASSERT_EQUAL_INT(4, candidates[0]);

    ServerSlotSnapshot unknown;
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(orphanCandidates(slots, unknown, journal).size()));
}

static void test_orphans_are_deleted_after_a_second_inventory() {
    Harness h;
    h.sensor.occupy(1);
    h.sensor.occupy(2);
    h.sensor.occupy(737);
    h.scan();
    h.inventory("{\"slots\":[1,2]}");
    h.inventory("{\"slots\":[1,2]}");
    const ReconciliationReport report = h.reconciler.run(JournalGuard(), false);
    ASSERT_ENUM(ReconciliationResult::Completed, report.result);
    TEST_ASSERT_EQUAL_INT(2, static_cast<int>(h.inventoryCalls()));
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(report.deleted));
    TEST_ASSERT_FALSE(h.sensor.occupied(737));
    TEST_ASSERT_TRUE(h.sensor.occupied(1));
    TEST_ASSERT_TRUE(h.sensor.occupied(2));
    ASSERT_ENUM(SlotState::Empty, h.slots.get(737));
    TEST_ASSERT_FALSE(h.logger.contains("737"));
}

static void test_nothing_to_do_uses_one_inventory_call() {
    Harness h;
    h.sensor.occupy(1);
    h.scan();
    h.inventory("{\"slots\":[1]}");
    const ReconciliationReport report = h.reconciler.run(JournalGuard(), false);
    ASSERT_ENUM(ReconciliationResult::Completed, report.result);
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(h.inventoryCalls()));
    TEST_ASSERT_EQUAL_INT(0, h.sensor.deleteCalls);
}

static void test_server_slots_including_pending_deletions_are_never_deleted() {
    Harness h;
    h.sensor.occupy(5);  // active
    h.sensor.occupy(6);  // pending deletion: its cleanup comes through deletions/pending
    h.scan();
    h.inventory("{\"slots\":[5,6]}");
    h.reconciler.run(JournalGuard(), true);
    TEST_ASSERT_EQUAL_INT(0, h.sensor.deleteCalls);
    TEST_ASSERT_TRUE(h.sensor.occupied(5));
    TEST_ASSERT_TRUE(h.sensor.occupied(6));
}

static void test_journal_slot_is_never_deleted() {
    Harness h;
    h.sensor.occupy(9);
    h.scan();
    h.inventory("{\"slots\":[]}");
    JournalGuard journal;
    journal.hasSlot = true;
    journal.slot = 9;
    h.reconciler.run(journal, true);
    TEST_ASSERT_TRUE(h.sensor.occupied(9));
}

static void test_offline_deletes_nothing() {
    Harness h;
    h.sensor.occupy(9);
    h.scan();
    h.transport.offline = true;
    const ReconciliationReport report = h.reconciler.run(JournalGuard(), true);
    ASSERT_ENUM(ReconciliationResult::InventoryUnavailable, report.result);
    TEST_ASSERT_TRUE(h.sensor.occupied(9));
    TEST_ASSERT_EQUAL_INT(0, h.sensor.deleteCalls);
}

static void test_unconfirmed_inventory_deletes_nothing() {
    const char* bodies[] = {"{\"slots\":[1,-1]}", "{}", "not json"};
    for (const char* body : bodies) {
        Harness h;
        h.sensor.occupy(9);
        h.scan();
        h.inventory(body);
        const ReconciliationReport report = h.reconciler.run(JournalGuard(), true);
        ASSERT_ENUM(ReconciliationResult::InventoryUnavailable, report.result);
        TEST_ASSERT_TRUE(h.sensor.occupied(9));
    }
    const int statuses[] = {401, 403, 429, 500};
    for (int status : statuses) {
        Harness h;
        h.sensor.occupy(9);
        h.scan();
        h.transport.queue(HttpMethod::Get, device_paths::kSlots, respond(status));
        const ReconciliationReport report = h.reconciler.run(JournalGuard(), true);
        ASSERT_ENUM(ReconciliationResult::InventoryUnavailable, report.result);
        TEST_ASSERT_EQUAL_INT(status, report.api.httpStatus);
        TEST_ASSERT_TRUE(h.sensor.occupied(9));
    }
}

static void test_failed_recheck_deletes_nothing() {
    Harness h;
    h.sensor.occupy(9);
    h.scan();
    h.inventory("{\"slots\":[]}");
    h.transport.queue(HttpMethod::Get, device_paths::kSlots, respond(503));
    const ReconciliationReport report = h.reconciler.run(JournalGuard(), false);
    ASSERT_ENUM(ReconciliationResult::InventoryUnavailable, report.result);
    TEST_ASSERT_TRUE(h.sensor.occupied(9));
}

static void test_slot_that_appears_in_the_recheck_is_kept() {
    Harness h;
    h.sensor.occupy(9);
    h.scan();
    h.inventory("{\"slots\":[]}");
    h.inventory("{\"slots\":[9]}");  // the enrollment of slot 9 was accepted in between
    const ReconciliationReport report = h.reconciler.run(JournalGuard(), false);
    ASSERT_ENUM(ReconciliationResult::Completed, report.result);
    TEST_ASSERT_TRUE(h.sensor.occupied(9));
    TEST_ASSERT_EQUAL_INT(1, static_cast<int>(report.skipped));
}

static void test_unreadable_journal_blocks_deletion() {
    Harness h;
    h.sensor.occupy(9);
    h.scan();
    h.inventory("{\"slots\":[]}");
    JournalGuard journal;
    journal.unreadable = true;
    const ReconciliationReport report = h.reconciler.run(journal, true);
    ASSERT_ENUM(ReconciliationResult::JournalUnreadable, report.result);
    TEST_ASSERT_TRUE(h.sensor.occupied(9));
}

static void test_many_candidates_need_operator_confirmation() {
    Harness h;
    for (int slot = 0; slot < 11; ++slot) {
        h.sensor.occupy(slot);
    }
    h.scan();
    h.inventory("{\"slots\":[]}");
    ReconciliationReport report = h.reconciler.run(JournalGuard(), false);
    ASSERT_ENUM(ReconciliationResult::NeedsConfirmation, report.result);
    TEST_ASSERT_EQUAL_INT(0, h.sensor.deleteCalls);

    h.inventory("{\"slots\":[]}");
    h.inventory("{\"slots\":[]}");
    report = h.reconciler.run(JournalGuard(), true);
    ASSERT_ENUM(ReconciliationResult::Completed, report.result);
    TEST_ASSERT_EQUAL_INT(11, static_cast<int>(report.deleted));
}

static void test_slot_already_empty_on_recheck_is_not_deleted_again() {
    Harness h;
    h.sensor.occupy(9);
    h.scan();
    h.sensor.templates[9] = false;  // cleared by something else after the scan
    h.inventory("{\"slots\":[]}");
    h.inventory("{\"slots\":[]}");
    const ReconciliationReport report = h.reconciler.run(JournalGuard(), false);
    ASSERT_ENUM(ReconciliationResult::Completed, report.result);
    TEST_ASSERT_EQUAL_INT(0, h.sensor.deleteCalls);
    ASSERT_ENUM(SlotState::Empty, h.slots.get(9));
}

static void test_unreadable_candidate_is_kept_and_reported() {
    Harness h;
    h.sensor.occupy(9);
    h.scan();
    h.sensor.probeOverrides[9] = SensorStatus::CommunicationError;
    h.inventory("{\"slots\":[]}");
    h.inventory("{\"slots\":[]}");
    const ReconciliationReport report = h.reconciler.run(JournalGuard(), false);
    ASSERT_ENUM(ReconciliationResult::SensorError, report.result);
    TEST_ASSERT_TRUE(h.sensor.occupied(9));
    TEST_ASSERT_EQUAL_INT(0, h.sensor.deleteCalls);
}

static void test_server_slots_beyond_capacity_are_ignored_and_counted() {
    Harness h;
    h.sensor.occupy(9);
    h.scan();
    h.inventory("{\"slots\":[9,1000,4000]}");
    const ReconciliationReport report = h.reconciler.run(JournalGuard(), false);
    ASSERT_ENUM(ReconciliationResult::Completed, report.result);
    TEST_ASSERT_EQUAL_INT(2, static_cast<int>(report.ignoredServerSlots));
    TEST_ASSERT_TRUE(h.sensor.occupied(9));
}

static void test_requires_a_completed_scan() {
    Harness h;
    h.slots.reset(1000);
    const ReconciliationReport report = h.reconciler.run(JournalGuard(), true);
    ASSERT_ENUM(ReconciliationResult::NotScanned, report.result);
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(h.inventoryCalls()));
}

int main(int, char**) {
    UNITY_BEGIN();
    RUN_TEST(test_orphan_candidates_exclude_server_and_journal_slots);
    RUN_TEST(test_orphans_are_deleted_after_a_second_inventory);
    RUN_TEST(test_nothing_to_do_uses_one_inventory_call);
    RUN_TEST(test_server_slots_including_pending_deletions_are_never_deleted);
    RUN_TEST(test_journal_slot_is_never_deleted);
    RUN_TEST(test_offline_deletes_nothing);
    RUN_TEST(test_unconfirmed_inventory_deletes_nothing);
    RUN_TEST(test_failed_recheck_deletes_nothing);
    RUN_TEST(test_slot_that_appears_in_the_recheck_is_kept);
    RUN_TEST(test_unreadable_journal_blocks_deletion);
    RUN_TEST(test_many_candidates_need_operator_confirmation);
    RUN_TEST(test_slot_already_empty_on_recheck_is_not_deleted_again);
    RUN_TEST(test_unreadable_candidate_is_kept_and_reported);
    RUN_TEST(test_server_slots_beyond_capacity_are_ignored_and_counted);
    RUN_TEST(test_requires_a_completed_scan);
    return UNITY_END();
}
