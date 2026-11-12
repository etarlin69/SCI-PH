#if defined(ARDUINO)

#include "fingerprint/r307/r307_sensor_esp32.h"

#include <Arduino.h>
#include <string.h>

namespace sciph {

namespace {

constexpr uint8_t kLoadChar = 0x07;
constexpr uint8_t kReadIndexTable = 0x1F;
constexpr uint8_t kProbeBuffer = 0x02;
// Ack payload of ReadIndexTable: confirmation code + 32 index bytes + 2 checksum bytes.
constexpr uint16_t kIndexReplyLength = 35;

}  // namespace

void R307Sensor::begin() {
    serial_.begin(baud_, SERIAL_8N1, rxPin_, txPin_);
    // The module needs about a second after power-up (the library's begin() waits the same).
    delay(1000);
}

void R307Sensor::drainInput() {
    delay(50);
    while (serial_.available() > 0) {
        serial_.read();
    }
}

SensorStatus R307Sensor::connect() {
    return finger_.verifyPassword() ? SensorStatus::Ok : SensorStatus::CommunicationError;
}

SensorStatus R307Sensor::readParameters(SensorParameters& parameters) {
    if (finger_.getParameters() != FINGERPRINT_OK) {
        return SensorStatus::CommunicationError;
    }
    capacity_ = finger_.capacity;
    parameters.capacity = finger_.capacity;
    parameters.securityLevel = finger_.security_level;
    parameters.packetLength = finger_.packet_len;
    parameters.baudRate = finger_.baud_rate;
    return SensorStatus::Ok;
}

SensorStatus R307Sensor::captureImage() {
    switch (finger_.getImage()) {
        case FINGERPRINT_OK:
            return SensorStatus::Ok;
        case FINGERPRINT_NOFINGER:
            return SensorStatus::NoFinger;
        case FINGERPRINT_IMAGEFAIL:
            return SensorStatus::LowQuality;
        default:
            return SensorStatus::CommunicationError;
    }
}

SensorStatus R307Sensor::imageToTemplate(uint8_t buffer) {
    switch (finger_.image2Tz(buffer)) {
        case FINGERPRINT_OK:
            return SensorStatus::Ok;
        case FINGERPRINT_IMAGEMESS:
        case FINGERPRINT_FEATUREFAIL:
        case FINGERPRINT_INVALIDIMAGE:
            return SensorStatus::LowQuality;
        default:
            return SensorStatus::CommunicationError;
    }
}

SensorStatus R307Sensor::createModel() {
    switch (finger_.createModel()) {
        case FINGERPRINT_OK:
            return SensorStatus::Ok;
        case FINGERPRINT_ENROLLMISMATCH:
            return SensorStatus::Mismatch;
        default:
            return SensorStatus::CommunicationError;
    }
}

SensorStatus R307Sensor::storeModel(int slot) {
    if (!inRange(slot)) {
        return SensorStatus::OutOfRange;
    }
    switch (finger_.storeModel(static_cast<uint16_t>(slot))) {
        case FINGERPRINT_OK:
            return SensorStatus::Ok;
        case FINGERPRINT_BADLOCATION:
            return SensorStatus::OutOfRange;
        case FINGERPRINT_FLASHERR:
            return SensorStatus::Failed;
        default:
            return SensorStatus::CommunicationError;
    }
}

SensorStatus R307Sensor::probeSlot(int slot) {
    if (!inRange(slot)) {
        return SensorStatus::OutOfRange;
    }
    uint8_t data[] = {kLoadChar, kProbeBuffer, static_cast<uint8_t>(slot >> 8), static_cast<uint8_t>(slot & 0xFF)};
    Adafruit_Fingerprint_Packet packet(FINGERPRINT_COMMANDPACKET, sizeof(data), data);
    finger_.writeStructuredPacket(packet);
    if (finger_.getStructuredPacket(&packet) != FINGERPRINT_OK || packet.type != FINGERPRINT_ACKPACKET) {
        drainInput();
        return SensorStatus::CommunicationError;
    }
    switch (packet.data[0]) {
        case FINGERPRINT_OK:
            return SensorStatus::Occupied;
        case FINGERPRINT_DBRANGEFAIL:
            // 0x0C "error reading the template / invalid template": an empty slot on the R307. To be confirmed on
            // each module (physical checklist): any other code is never read as empty.
            return SensorStatus::Empty;
        case FINGERPRINT_BADLOCATION:
            return SensorStatus::OutOfRange;
        case FINGERPRINT_PACKETRECIEVEERR:
            return SensorStatus::CommunicationError;
        default:
            return SensorStatus::Failed;
    }
}

SensorStatus R307Sensor::deleteModel(int slot) {
    if (!inRange(slot)) {
        return SensorStatus::OutOfRange;
    }
    switch (finger_.deleteModel(static_cast<uint16_t>(slot))) {
        case FINGERPRINT_OK:
            return SensorStatus::Ok;
        case FINGERPRINT_DELETEFAIL:
        case FINGERPRINT_BADLOCATION:
            return SensorStatus::Failed;
        default:
            return SensorStatus::CommunicationError;
    }
}

SearchResult R307Sensor::search() {
    SearchResult result;
    switch (finger_.fingerSearch(1)) {
        case FINGERPRINT_OK:
            result.status = SensorStatus::Ok;
            result.slot = finger_.fingerID;
            break;
        case FINGERPRINT_NOTFOUND:
            result.status = SensorStatus::NoMatch;
            break;
        default:
            result.status = SensorStatus::CommunicationError;
            break;
    }
    return result;
}

SensorStatus R307Sensor::templateCount(uint16_t& count) {
    if (finger_.getTemplateCount() != FINGERPRINT_OK) {
        return SensorStatus::CommunicationError;
    }
    count = finger_.templateCount;
    return SensorStatus::Ok;
}

SensorStatus R307Sensor::readIndexPage(uint8_t page, uint8_t* bits32) {
    uint8_t data[] = {kReadIndexTable, page};
    Adafruit_Fingerprint_Packet packet(FINGERPRINT_COMMANDPACKET, sizeof(data), data);
    finger_.writeStructuredPacket(packet);
    if (finger_.getStructuredPacket(&packet) != FINGERPRINT_OK || packet.type != FINGERPRINT_ACKPACKET ||
        packet.length != kIndexReplyLength || packet.data[0] != FINGERPRINT_OK) {
        // Not implemented by this module (no answer, an error code or another layout): fall back to LoadChar.
        drainInput();
        return SensorStatus::Unsupported;
    }
    memcpy(bits32, &packet.data[1], 32);
    return SensorStatus::Ok;
}

}  // namespace sciph

#endif
