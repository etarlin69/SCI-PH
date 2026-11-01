// Operation scheduling: one physical operation at a time, priority, intervals, 429 Retry-After, 403 suspension and
// the request budget.
#include <unity.h>

#include "../support/fakes.h"
#include "state/operation_scheduler.h"

using namespace sciph;
using namespace sciph::testing;

void setUp() {}
void tearDown() {}

static SchedulerSettings settings() {
    SchedulerSettings s;
    s.reconciliationIntervalMs = 0;
    return s;
}

static void test_nothing_runs_before_start() {
    OperationScheduler scheduler(settings());
    ASSERT_ENUM(Operation::None, scheduler.next(0, PendingWork()));
}

static void test_priority_identification_enrollment_deletion_reconciliation_heartbeat() {
    OperationScheduler scheduler(settings());
    scheduler.start(0);
    scheduler.requestReconciliation(0);
    PendingWork work;

    ASSERT_ENUM(Operation::IdentificationPoll, scheduler.next(0, work));
    scheduler.markRan(Operation::IdentificationPoll, 0);
    ASSERT_ENUM(Operation::EnrollmentPoll, scheduler.next(0, work));
    scheduler.markRan(Operation::EnrollmentPoll, 0);
    ASSERT_ENUM(Operation::DeletionPoll, scheduler.next(0, work));
    scheduler.markRan(Operation::DeletionPoll, 0);
    ASSERT_ENUM(Operation::Reconciliation, scheduler.next(0, work));
    scheduler.reconciliationRan(0, true);
    ASSERT_ENUM(Operation::Heartbeat, scheduler.next(0, work));
    scheduler.markRan(Operation::Heartbeat, 0);
    ASSERT_ENUM(Operation::None, scheduler.next(0, work));
}

static void test_replays_come_before_new_work_of_their_kind() {
    OperationScheduler scheduler(settings());
    scheduler.start(0);
    PendingWork work;
    work.identificationReplayDue = true;
    work.identificationBusy = true;
    work.enrollmentReplayDue = true;
    work.enrollmentBusy = true;
    work.deletionReplayDue = true;
    work.deletionBusy = true;
    ASSERT_ENUM(Operation::IdentificationReplay, scheduler.next(0, work));
    work.identificationReplayDue = false;
    // A pending identification result blocks new sessions but not enrollment work.
    ASSERT_ENUM(Operation::EnrollmentReplay, scheduler.next(0, work));
    work.enrollmentReplayDue = false;
    ASSERT_ENUM(Operation::DeletionReplay, scheduler.next(0, work));
    work.deletionReplayDue = false;
    ASSERT_ENUM(Operation::Heartbeat, scheduler.next(0, work));
}

static void test_identification_always_wins_when_due() {
    OperationScheduler scheduler(settings());
    scheduler.start(0);
    PendingWork work;
    work.enrollmentReplayDue = true;
    work.enrollmentBusy = true;
    ASSERT_ENUM(Operation::IdentificationPoll, scheduler.next(0, work));
}

static void test_poll_intervals() {
    OperationScheduler scheduler(settings());
    scheduler.start(0);
    PendingWork work;
    scheduler.markRan(Operation::IdentificationPoll, 0);
    scheduler.markRan(Operation::EnrollmentPoll, 0);
    scheduler.markRan(Operation::DeletionPoll, 0);
    scheduler.markRan(Operation::Heartbeat, 0);
    ASSERT_ENUM(Operation::None, scheduler.next(2999, work));
    ASSERT_ENUM(Operation::IdentificationPoll, scheduler.next(3000, work));
    scheduler.markRan(Operation::IdentificationPoll, 3000);
    ASSERT_ENUM(Operation::EnrollmentPoll, scheduler.next(3000, work));
    scheduler.markRan(Operation::EnrollmentPoll, 3000);
    ASSERT_ENUM(Operation::None, scheduler.next(3000, work));

    scheduler.markRan(Operation::IdentificationPoll, 29000);
    scheduler.markRan(Operation::EnrollmentPoll, 29000);
    ASSERT_ENUM(Operation::None, scheduler.next(29999, work));
    ASSERT_ENUM(Operation::DeletionPoll, scheduler.next(30000, work));
    scheduler.markRan(Operation::DeletionPoll, 30000);
    ASSERT_ENUM(Operation::None, scheduler.next(31999, work));
    ASSERT_ENUM(Operation::IdentificationPoll, scheduler.next(32000, work));
}

static void test_429_honors_retry_after_for_every_endpoint() {
    OperationScheduler scheduler(settings());
    scheduler.start(0);
    PendingWork work;
    work.identificationReplayDue = true;
    scheduler.throttle(1000, 17);
    TEST_ASSERT_TRUE(scheduler.throttled(1000));
    ASSERT_ENUM(Operation::None, scheduler.next(17999, work));
    ASSERT_ENUM(Operation::IdentificationReplay, scheduler.next(18000, work));
}

static void test_429_without_retry_after_uses_default_and_cap() {
    SchedulerSettings s = settings();
    OperationScheduler scheduler(s);
    scheduler.start(0);
    scheduler.throttle(0, -1);
    TEST_ASSERT_TRUE(scheduler.throttled(s.defaultRetryAfterMs - 1));
    TEST_ASSERT_FALSE(scheduler.throttled(s.defaultRetryAfterMs));
    scheduler.throttle(0, 100000);
    TEST_ASSERT_FALSE(scheduler.throttled(s.maxRetryAfterMs));
}

static void test_403_suspends_only_its_scope_family() {
    SchedulerSettings s = settings();
    OperationScheduler scheduler(s);
    scheduler.start(0);
    PendingWork work;
    scheduler.suspend(ScopeFamily::Enroll, 0);
    ASSERT_ENUM(Operation::IdentificationPoll, scheduler.next(0, work));
    scheduler.markRan(Operation::IdentificationPoll, 0);
    // Enrollment, deletion and reconciliation are paused; heartbeat continues.
    ASSERT_ENUM(Operation::Heartbeat, scheduler.next(0, work));
    scheduler.markRan(Operation::Heartbeat, 0);
    TEST_ASSERT_TRUE(scheduler.suspended(ScopeFamily::Enroll, s.forbiddenRetryMs - 1));
    TEST_ASSERT_FALSE(scheduler.suspended(ScopeFamily::Enroll, s.forbiddenRetryMs));
    scheduler.resumeAll();
    TEST_ASSERT_FALSE(scheduler.suspended(ScopeFamily::Enroll, 1));
}

static void test_scope_families() {
    ASSERT_ENUM(ScopeFamily::Identify, scopeFamilyOf(Operation::IdentificationPoll));
    ASSERT_ENUM(ScopeFamily::Enroll, scopeFamilyOf(Operation::EnrollmentReplay));
    ASSERT_ENUM(ScopeFamily::Enroll, scopeFamilyOf(Operation::DeletionPoll));
    ASSERT_ENUM(ScopeFamily::Enroll, scopeFamilyOf(Operation::Reconciliation));
    ASSERT_ENUM(ScopeFamily::Heartbeat, scopeFamilyOf(Operation::Heartbeat));
}

static void test_reconciliation_retry_and_period() {
    SchedulerSettings s = settings();
    s.reconciliationIntervalMs = 1000000;
    s.identificationPollMs = 100000000;
    s.enrollmentPollMs = 100000000;
    s.deletionPollMs = 100000000;
    s.heartbeatMs = 100000000;
    OperationScheduler scheduler(s);
    scheduler.start(0);
    scheduler.markRan(Operation::IdentificationPoll, 0);
    scheduler.markRan(Operation::EnrollmentPoll, 0);
    scheduler.markRan(Operation::DeletionPoll, 0);
    scheduler.markRan(Operation::Heartbeat, 0);
    PendingWork work;
    ASSERT_ENUM(Operation::None, scheduler.next(1, work));
    scheduler.requestReconciliation(1);
    ASSERT_ENUM(Operation::Reconciliation, scheduler.next(1, work));
    scheduler.reconciliationRan(1, false);
    ASSERT_ENUM(Operation::None, scheduler.next(s.reconciliationRetryMs, work));
    ASSERT_ENUM(Operation::Reconciliation, scheduler.next(1 + s.reconciliationRetryMs, work));
}

// Every scheduled call is one request (a poll that finds work adds a result POST). Over one minute with all polls
// due as often as allowed, the reader stays far below the 120 requests / 60 s device limit.
static void test_request_budget_under_120_per_minute() {
    OperationScheduler scheduler(settings());
    scheduler.start(0);
    PendingWork work;
    int requests = 0;
    for (uint64_t now = 0; now < 60000; now += 10) {
        const Operation operation = scheduler.next(now, work);
        if (operation == Operation::None) {
            continue;
        }
        ++requests;
        scheduler.markRan(operation, now);
    }
    TEST_ASSERT_TRUE(requests <= 50);
    TEST_ASSERT_TRUE(requests < 120);
}

int main(int, char**) {
    UNITY_BEGIN();
    RUN_TEST(test_nothing_runs_before_start);
    RUN_TEST(test_priority_identification_enrollment_deletion_reconciliation_heartbeat);
    RUN_TEST(test_replays_come_before_new_work_of_their_kind);
    RUN_TEST(test_identification_always_wins_when_due);
    RUN_TEST(test_poll_intervals);
    RUN_TEST(test_429_honors_retry_after_for_every_endpoint);
    RUN_TEST(test_429_without_retry_after_uses_default_and_cap);
    RUN_TEST(test_403_suspends_only_its_scope_family);
    RUN_TEST(test_scope_families);
    RUN_TEST(test_reconciliation_retry_and_period);
    RUN_TEST(test_request_budget_under_120_per_minute);
    return UNITY_END();
}
