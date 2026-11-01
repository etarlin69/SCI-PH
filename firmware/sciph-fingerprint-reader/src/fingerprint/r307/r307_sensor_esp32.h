#pragma once

#if defined(ARDUINO)

#include <Adafruit_Fingerprint.h>
#include <HardwareSerial.h>

#include "fingerprint/r307/fingerprint_sensor.h"

namespace sciph {

// R307 over HardwareSerial with the Adafruit Fingerprint Sensor Library 2.1.4. LoadChar into CharBuffer 2 and
// ReadIndexTable are sent as raw packets (the library loads into CharBuffer 1 and has no index command). Search uses
// fingerSearch over the reported capacity: the library's fingerFastSearch only covers the first 163 slots.
class R307Sensor : public IFingerprintSensor {
public:
    R307Sensor(Adafruit_Fingerprint& finger, HardwareSerial& serial, int rxPin, int txPin, uint32_t baud)
        : finger_(finger), serial_(serial), rxPin_(rxPin), txPin_(txPin), baud_(baud) {}

    // Opens the UART (8N1 on the configured pins) and lets the module boot.
    void begin();

    SensorStatus connect() override;
    SensorStatus readParameters(SensorParameters& parameters) override;
    SensorStatus captureImage() override;
    SensorStatus imageToTemplate(uint8_t buffer) override;
    SensorStatus createModel() override;
    SensorStatus storeModel(int slot) override;
    SensorStatus probeSlot(int slot) override;
    SensorStatus deleteModel(int slot) override;
    SearchResult search() override;
    SensorStatus templateCount(uint16_t& count) override;
    SensorStatus readIndexPage(uint8_t page, uint8_t* bits32) override;

private:
    bool inRange(int slot) const { return slot >= 0 && slot < static_cast<int>(capacity_); }
    void drainInput();

    Adafruit_Fingerprint& finger_;
    HardwareSerial& serial_;
    int rxPin_;
    int txPin_;
    uint32_t baud_;
    uint16_t capacity_ = 0;
};

}  // namespace sciph

#endif
