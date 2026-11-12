#include "fingerprint/r307/fingerprint_sensor.h"

namespace sciph {

const char* sensorStatusName(SensorStatus status) {
    switch (status) {
        case SensorStatus::Ok:
            return "ok";
        case SensorStatus::NoFinger:
            return "no_finger";
        case SensorStatus::LowQuality:
            return "low_quality";
        case SensorStatus::Mismatch:
            return "mismatch";
        case SensorStatus::NoMatch:
            return "no_match";
        case SensorStatus::Empty:
            return "empty";
        case SensorStatus::Occupied:
            return "occupied";
        case SensorStatus::OutOfRange:
            return "out_of_range";
        case SensorStatus::Unsupported:
            return "unsupported";
        case SensorStatus::CommunicationError:
            return "sensor_communication";
        case SensorStatus::Failed:
            return "sensor_failed";
    }
    return "sensor_failed";
}

}  // namespace sciph
