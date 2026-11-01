#pragma once

namespace sciph {

enum class ReaderState {
    Boot,
    Provisioning,
    WifiConnect,
    NetworkRecovery,
    TimeSync,
    SensorInit,
    ReadParameters,
    SlotReconciliation,
    JournalRecovery,
    Idle,
    AuthFailed,
    SensorError
};

inline const char* readerStateName(ReaderState state) {
    switch (state) {
        case ReaderState::Boot:
            return "BOOT";
        case ReaderState::Provisioning:
            return "PROVISIONING";
        case ReaderState::WifiConnect:
            return "WIFI_CONNECT";
        case ReaderState::NetworkRecovery:
            return "NETWORK_RECOVERY";
        case ReaderState::TimeSync:
            return "TIME_SYNC";
        case ReaderState::SensorInit:
            return "SENSOR_INIT";
        case ReaderState::ReadParameters:
            return "READ_PARAMETERS";
        case ReaderState::SlotReconciliation:
            return "SLOT_RECONCILIATION";
        case ReaderState::JournalRecovery:
            return "JOURNAL_RECOVERY";
        case ReaderState::Idle:
            return "IDLE";
        case ReaderState::AuthFailed:
            return "AUTH_FAILED";
        case ReaderState::SensorError:
            return "SENSOR_ERROR";
    }
    return "UNKNOWN";
}

}  // namespace sciph
