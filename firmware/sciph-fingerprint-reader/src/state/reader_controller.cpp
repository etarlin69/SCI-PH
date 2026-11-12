#include "state/reader_controller.h"

namespace sciph {

namespace {

constexpr const char* kOperation = "reader";

const char* familyName(ScopeFamily family) {
    switch (family) {
        case ScopeFamily::Identify:
            return "identify";
        case ScopeFamily::Enroll:
            return "enroll";
        case ScopeFamily::Heartbeat:
            return "heartbeat";
    }
    return "unknown";
}

}  // namespace

ReaderController::ReaderController(const ReaderDependencies& dependencies, const ControllerSettings& settings,
                                   const char* firmwareVersion)
    : deps_(dependencies),
      settings_(settings),
      firmwareVersion_(firmwareVersion),
      wifiBackoff_(settings.wifiBackoffInitialMs, settings.wifiBackoffMaxMs) {}

void ReaderController::begin(bool provisioned, bool requireTime) {
    provisioned_ = provisioned;
    requireTime_ = requireTime;
    enter(ReaderState::Boot);
}

void ReaderController::enter(ReaderState state) {
    state_ = state;
    enteredAtMs_ = deps_.clock.monotonicMs();
    deps_.logger.write(LogLevel::Info, "state", readerStateName(state));
    switch (state) {
        case ReaderState::Provisioning:
            deps_.feedback.signal(FeedbackSignal::Provisioning);
            break;
        case ReaderState::WifiConnect:
            deps_.network.begin();
            break;
        case ReaderState::NetworkRecovery:
            deps_.feedback.signal(FeedbackSignal::NetworkError);
            wifiBackoff_.reset();
            nextWifiAttemptMs_ = enteredAtMs_ + wifiBackoff_.nextDelayMs();
            break;
        case ReaderState::TimeSync:
            timeWarningLogged_ = false;
            deps_.network.startTimeSync();
            break;
        case ReaderState::AuthFailed:
            deps_.feedback.signal(FeedbackSignal::AuthenticationFailed);
            nextAuthProbeMs_ = enteredAtMs_ + settings_.authRetryMs;
            break;
        case ReaderState::SensorError:
            deps_.feedback.signal(FeedbackSignal::SensorError);
            nextSensorRetryMs_ = enteredAtMs_ + settings_.sensorRetryMs;
            break;
        case ReaderState::Idle:
            deps_.feedback.signal(FeedbackSignal::Ready);
            break;
        default:
            break;
    }
}

void ReaderController::tick() {
    const uint64_t now = deps_.clock.monotonicMs();
    switch (state_) {
        case ReaderState::Boot:
            enter(provisioned_ ? ReaderState::WifiConnect : ReaderState::Provisioning);
            return;

        case ReaderState::Provisioning:
            // The serial console completes provisioning and restarts the reader.
            return;

        case ReaderState::WifiConnect:
            if (deps_.network.connected()) {
                wifiBackoff_.reset();
                enter(ReaderState::TimeSync);
            } else if (now - enteredAtMs_ >= settings_.wifiConnectTimeoutMs) {
                enter(ReaderState::NetworkRecovery);
            }
            return;

        case ReaderState::NetworkRecovery:
            if (deps_.network.connected()) {
                wifiBackoff_.reset();
                deps_.logger.write(LogLevel::Info, kOperation, "wifi connected");
                enter(bootCompleted_ ? ReaderState::Idle : ReaderState::TimeSync);
                return;
            }
            if (now >= nextWifiAttemptMs_) {
                deps_.network.reconnect();
                nextWifiAttemptMs_ = now + wifiBackoff_.nextDelayMs();
            }
            return;

        case ReaderState::TimeSync: {
            if (!deps_.network.connected()) {
                enter(ReaderState::NetworkRecovery);
                return;
            }
            int64_t epoch = 0;
            if (deps_.clock.utcNow(epoch) && isPlausibleUtc(epoch)) {
                enter(ReaderState::SensorInit);
                return;
            }
            if (now - enteredAtMs_ >= settings_.timeSyncWarningMs) {
                if (!requireTime_) {
                    // Development HTTP: no certificate to validate. Expiry decisions stay with the server.
                    deps_.logger.write(LogLevel::Warning, kOperation, "time unavailable (time_unavailable), continuing");
                    enter(ReaderState::SensorInit);
                    return;
                }
                if (!timeWarningLogged_) {
                    timeWarningLogged_ = true;
                    deps_.logger.write(LogLevel::Warning, kOperation,
                                       "time unavailable (time_unavailable): HTTPS waits for SNTP");
                }
            }
            return;
        }

        case ReaderState::SensorInit:
            if (deps_.sensor.connect() == SensorStatus::Ok) {
                enter(ReaderState::ReadParameters);
            } else {
                deps_.logger.write(LogLevel::Error, kOperation, "sensor not responding (sensor_communication)");
                enter(ReaderState::SensorError);
            }
            return;

        case ReaderState::ReadParameters: {
            SensorParameters parameters;
            if (deps_.sensor.readParameters(parameters) != SensorStatus::Ok || parameters.capacity == 0 ||
                parameters.capacity > settings_.maxCapacity) {
                deps_.logger.write(LogLevel::Error, kOperation, "sensor parameters unavailable or implausible");
                enter(ReaderState::SensorError);
                return;
            }
            // The capacity reported by this sensor, never a constant. The map is rebuilt from the sensor every time
            // (boot or recovery from a sensor error, which may mean a replaced module).
            capacity_ = parameters.capacity;
            deps_.slots.reset(capacity_);
            deps_.logger.write(LogLevel::Info, kOperation, "sensor capacity " + std::to_string(capacity_));
            enter(ReaderState::SlotReconciliation);
            return;
        }

        case ReaderState::SlotReconciliation: {
            if (!deps_.slots.scanned()) {
                deps_.logger.write(LogLevel::Info, kOperation, "reading physical slot occupancy");
                const ScanReport scan = deps_.scanner.scan(deps_.slots);
                if (!scan.completed) {
                    deps_.logger.write(LogLevel::Error, kOperation, "slot scan failed (sensor_communication)");
                    enter(ReaderState::SensorError);
                    return;
                }
                deps_.logger.write(LogLevel::Info, kOperation,
                                   "slot scan done: " + std::to_string(scan.occupied) + " occupied, " +
                                       std::to_string(scan.unknown) + " unreadable, index table " +
                                       (scan.usedIndexTable ? "yes" : "no"));
            }
            // The journal is read before reconciling: its slot is never an orphan, even before recovery resolves it.
            deps_.enrollment.loadJournal();
            const ReconciliationReport report = deps_.reconciler.run(deps_.enrollment.journalGuard(), false);
            handleReconciliation(report, deps_.clock.monotonicMs());
            if (state_ == ReaderState::SlotReconciliation) {
                enter(ReaderState::JournalRecovery);
            }
            return;
        }

        case ReaderState::JournalRecovery: {
            const OperationReport enrollment = deps_.enrollment.recover();
            handle(enrollment, ScopeFamily::Enroll);
            if (state_ != ReaderState::JournalRecovery) {
                return;
            }
            const OperationReport deletion = deps_.deletion.recover();
            handle(deletion, ScopeFamily::Enroll);
            if (state_ != ReaderState::JournalRecovery) {
                return;
            }
            const uint64_t after = deps_.clock.monotonicMs();
            if (enrollment.journalResolved) {
                deps_.scheduler.requestReconciliation(after);
            }
            bootCompleted_ = true;
            deps_.scheduler.start(after);
            enter(ReaderState::Idle);
            return;
        }

        case ReaderState::Idle: {
            if (!deps_.network.connected()) {
                enter(ReaderState::NetworkRecovery);
                return;
            }
            PendingWork work;
            work.identificationReplayDue = deps_.identification.replayDue(now);
            work.identificationBusy = deps_.identification.replayPending();
            work.enrollmentReplayDue = deps_.enrollment.replayDue(now);
            work.enrollmentBusy = deps_.enrollment.busy();
            work.deletionReplayDue = deps_.deletion.replayDue(now);
            work.deletionBusy = deps_.deletion.busy();
            runOperation(deps_.scheduler.next(now, work));
            return;
        }

        case ReaderState::AuthFailed: {
            if (now < nextAuthProbeMs_) {
                return;
            }
            nextAuthProbeMs_ = now + settings_.authRetryMs;
            if (!deps_.network.connected()) {
                return;
            }
            const ApiResponseInfo info = deps_.api.heartbeat(firmwareVersion_, uptimeSeconds());
            if (info.status == ApiStatus::NoContent || info.status == ApiStatus::Forbidden) {
                // Authenticated again (403 = authenticated without the heartbeat scope).
                deps_.logger.write(LogLevel::Info, kOperation, "credential accepted again");
                deps_.scheduler.resumeAll();
                enter(bootCompleted_ ? ReaderState::Idle : ReaderState::JournalRecovery);
            } else {
                deps_.logger.write(LogLevel::Warning, kOperation,
                                   std::string("credential still rejected (") + apiStatusName(info.status) + ")");
            }
            return;
        }

        case ReaderState::SensorError:
            if (now < nextSensorRetryMs_) {
                return;
            }
            nextSensorRetryMs_ = now + settings_.sensorRetryMs;
            if (deps_.sensor.connect() == SensorStatus::Ok) {
                deps_.logger.write(LogLevel::Info, kOperation, "sensor responding again");
                enter(ReaderState::ReadParameters);
            }
            return;
    }
}

void ReaderController::runOperation(Operation operation) {
    OperationReport report;
    switch (operation) {
        case Operation::None:
            return;
        case Operation::IdentificationReplay:
            report = deps_.identification.replay();
            break;
        case Operation::IdentificationPoll:
            report = deps_.identification.poll();
            deps_.scheduler.markRan(operation, deps_.clock.monotonicMs());
            break;
        case Operation::EnrollmentReplay:
            report = deps_.enrollment.replay();
            if (report.journalResolved) {
                // An interrupted enrollment was resolved: reconcile afterwards (contract, "Reglas del lector").
                deps_.scheduler.requestReconciliation(deps_.clock.monotonicMs());
            }
            break;
        case Operation::EnrollmentPoll:
            report = deps_.enrollment.poll();
            deps_.scheduler.markRan(operation, deps_.clock.monotonicMs());
            break;
        case Operation::DeletionReplay:
            report = deps_.deletion.replay();
            break;
        case Operation::DeletionPoll:
            report = deps_.deletion.poll(deps_.enrollment.journalGuard());
            deps_.scheduler.markRan(operation, deps_.clock.monotonicMs());
            break;
        case Operation::Reconciliation: {
            const ReconciliationReport reconciliation =
                deps_.reconciler.run(deps_.enrollment.journalGuard(), allowLargeDeletion_);
            allowLargeDeletion_ = false;
            handleReconciliation(reconciliation, deps_.clock.monotonicMs());
            return;
        }
        case Operation::Heartbeat:
            report.record(deps_.api.heartbeat(firmwareVersion_, uptimeSeconds()));
            deps_.scheduler.markRan(operation, deps_.clock.monotonicMs());
            break;
    }
    handle(report, scopeFamilyOf(operation));
}

void ReaderController::handleReconciliation(const ReconciliationReport& report, uint64_t now) {
    lastReconciliation_ = report.result;
    // Needs confirmation or an unreadable journal wait for the operator, not for a retry loop.
    const bool settled = report.result == ReconciliationResult::Completed ||
                         report.result == ReconciliationResult::NeedsConfirmation ||
                         report.result == ReconciliationResult::JournalUnreadable;
    deps_.scheduler.reconciliationRan(now, settled);

    OperationReport operation;
    if (report.result != ReconciliationResult::NotScanned) {
        operation.record(report.api);
    }
    operation.sensorFault = report.result == ReconciliationResult::SensorError;
    handle(operation, ScopeFamily::Enroll);
}

void ReaderController::handle(const OperationReport& report, ScopeFamily family) {
    const uint64_t now = deps_.clock.monotonicMs();
    if (report.apiCalled) {
        switch (report.api.status) {
            case ApiStatus::Unauthorized:
                deps_.logger.write(LogLevel::Warning, kOperation, "credential rejected (unauthorized)");
                enterAuthFailed();
                return;
            case ApiStatus::Forbidden:
                deps_.scheduler.suspend(family, now);
                deps_.logger.write(LogLevel::Warning, kOperation,
                                   std::string("missing_scope: ") + familyName(family) + " paused");
                break;
            case ApiStatus::RateLimited:
                deps_.scheduler.throttle(now, report.api.retryAfterSeconds);
                deps_.logger.write(LogLevel::Warning, kOperation,
                                   "rate_limited, Retry-After " + std::to_string(report.api.retryAfterSeconds) + " s");
                break;
            case ApiStatus::NoResponse:
            case ApiStatus::ServerError:
                deps_.logger.write(LogLevel::Warning, kOperation, apiStatusName(report.api.status));
                break;
            default:
                break;
        }
    }
    if (report.sensorFault) {
        onSensorFault();
    }
}

void ReaderController::enterAuthFailed() {
    if (state_ != ReaderState::AuthFailed) {
        enter(ReaderState::AuthFailed);
    }
}

void ReaderController::onSensorFault() {
    if (deps_.sensor.connect() != SensorStatus::Ok) {
        deps_.logger.write(LogLevel::Error, kOperation, "sensor not responding (sensor_communication)");
        enter(ReaderState::SensorError);
    }
}

void ReaderController::requestReconciliation(bool allowLargeDeletion) {
    allowLargeDeletion_ = allowLargeDeletion;
    deps_.scheduler.requestReconciliation(deps_.clock.monotonicMs());
}

uint32_t ReaderController::uptimeSeconds() const {
    return static_cast<uint32_t>(deps_.clock.monotonicMs() / 1000);
}

std::vector<std::string> ReaderController::statusLines() const {
    std::vector<std::string> lines;
    lines.push_back(std::string("estado: ") + readerStateName(state_));
    if (capacity_ > 0) {
        lines.push_back("capacidad del sensor: " + std::to_string(capacity_));
        lines.push_back("slots ocupados (fisicos): " + std::to_string(deps_.slots.count(SlotState::Occupied)) +
                        ", ilegibles: " + std::to_string(deps_.slots.count(SlotState::Unknown)));
    }
    lines.push_back("inventario del servidor: " +
                    (deps_.serverSlots.known() ? std::to_string(deps_.serverSlots.size()) + " slots"
                                               : std::string("sin confirmar")));
    lines.push_back(std::string("ultima reconciliacion: ") + reconciliationResultName(lastReconciliation_));
    lines.push_back(std::string("registro en curso: ") + deps_.enrollment.journalStatus());
    lines.push_back(std::string("borrado sin confirmar: ") + (deps_.deletion.busy() ? "si" : "no"));
    const uint64_t now = deps_.clock.monotonicMs();
    if (deps_.scheduler.throttled(now)) {
        lines.push_back("limite de peticiones (429): " +
                        std::to_string((deps_.scheduler.throttledUntil() - now) / 1000) + " s");
    }
    return lines;
}

}  // namespace sciph
