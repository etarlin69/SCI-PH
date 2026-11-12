#pragma once

#include <cstdint>

namespace sciph {

enum class SensorStatus : uint8_t {
    Ok,
    NoFinger,
    LowQuality,
    Mismatch,
    NoMatch,
    Empty,
    Occupied,
    OutOfRange,
    Unsupported,
    CommunicationError,
    Failed
};

const char* sensorStatusName(SensorStatus status);

struct SensorParameters {
    uint16_t capacity = 0;
    uint16_t securityLevel = 0;
    uint16_t packetLength = 0;
    uint32_t baudRate = 0;
};

struct SearchResult {
    SensorStatus status = SensorStatus::Failed;
    int slot = -1;
};

// The R307 behind an interface so every flow runs against a simulated sensor in native tests. Templates and images
// never leave the sensor: nothing here returns them, and nothing sends them to SCI-PH.
class IFingerprintSensor {
public:
    virtual ~IFingerprintSensor() = default;

    // Handshake (VfyPwd): Ok or CommunicationError.
    virtual SensorStatus connect() = 0;
    // ReadSysPara: the capacity comes from the sensor, never from a constant.
    virtual SensorStatus readParameters(SensorParameters& parameters) = 0;
    // GenImg: Ok, NoFinger, LowQuality (imaging failed) or CommunicationError.
    virtual SensorStatus captureImage() = 0;
    // Img2Tz into CharBuffer 1 or 2: Ok, LowQuality or CommunicationError.
    virtual SensorStatus imageToTemplate(uint8_t buffer) = 0;
    // RegModel (CharBuffer 1 + 2): Ok, Mismatch or CommunicationError.
    virtual SensorStatus createModel() = 0;
    // Store CharBuffer 1: Ok, OutOfRange, Failed or CommunicationError. The R307 overwrites an occupied slot, so
    // callers always probe the slot right before.
    virtual SensorStatus storeModel(int slot) = 0;
    // LoadChar into CharBuffer 2 (CharBuffer 1 keeps the model being enrolled): Occupied, Empty, OutOfRange,
    // CommunicationError, or Failed for any answer whose meaning is unknown (never treated as empty).
    virtual SensorStatus probeSlot(int slot) = 0;
    // DeletChar of one slot: Ok, Failed or CommunicationError.
    virtual SensorStatus deleteModel(int slot) = 0;
    // Search CharBuffer 1 over the whole library: Ok with the slot, NoMatch or CommunicationError. No score is used.
    virtual SearchResult search() = 0;
    virtual SensorStatus templateCount(uint16_t& count) = 0;
    // ReadIndexTable (0x1F), 256 slots per page, bit n of byte k = slot page*256 + k*8 + n: Ok, Unsupported or
    // CommunicationError. Many R307 clones do not implement it.
    virtual SensorStatus readIndexPage(uint8_t page, uint8_t* bits32) = 0;
};

}  // namespace sciph
