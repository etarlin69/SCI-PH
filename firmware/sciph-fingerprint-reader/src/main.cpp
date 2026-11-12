// SCI-PH fingerprint reader: ESP32 + R307 client of the existing Device API. Wiring only; the logic lives in the
// portable modules (state/, fingerprint/, api/, storage/), which the native tests exercise.
#if defined(ARDUINO)

#include <Adafruit_Fingerprint.h>
#include <Arduino.h>
#include <HardwareSerial.h>

#include <string>

#include "api/device_api_client.h"
#include "api/http_transport_esp32.h"
#include "config/device_config.h"
#include "config/reader_settings.h"
#include "core/logger.h"
#include "core/platform_esp32.h"
#include "feedback/feedback.h"
#include "feedback/gpio_indicator_esp32.h"
#include "fingerprint/cleanup/deletion_attempt.h"
#include "fingerprint/cleanup/deletion_coordinator.h"
#include "fingerprint/enrollment/enrollment_coordinator.h"
#include "fingerprint/identification/identification_coordinator.h"
#include "fingerprint/r307/r307_sensor_esp32.h"
#include "fingerprint/reconciliation/physical_slot_scanner.h"
#include "fingerprint/reconciliation/slot_reconciler.h"
#include "firmware_config.h"
#include "network/wifi_link_esp32.h"
#include "provisioning/serial_console.h"
#include "state/operation_scheduler.h"
#include "state/reader_controller.h"
#include "storage/journal/enrollment_journal.h"
#include "storage/nvs_key_value_store_esp32.h"
#include "storage/slot_map/slot_map.h"

using namespace sciph;

namespace {

constexpr uint32_t kHttpTimeoutMs = 10000;
constexpr size_t kMaxConsoleLine = 4096;

const CaptureSettings captureSettings{};
const ReplaySettings replaySettings{};
const SchedulerSettings schedulerSettings{};
const ReconciliationSettings reconciliationSettings{};
const ControllerSettings controllerSettings{};

SerialOutput console;
SystemClock systemClock;
HardwareRandom hardwareRandom;
NvsKeyValueStore nvs;
ConsoleLogger logger(console);

ConfigRepository configs(nvs);
DeviceConfig config;
bool provisioned = false;

GpioIndicator indicator(SCIPH_LED_PIN, SCIPH_BUZZER_PIN, SCIPH_LED_ACTIVE_HIGH != 0);
FeedbackController feedback(console, &indicator, systemClock);

HardwareSerial sensorSerial(SCIPH_R307_UART);
Adafruit_Fingerprint fingerprint(&sensorSerial, SCIPH_R307_PASSWORD);
R307Sensor sensor(fingerprint, sensorSerial, SCIPH_R307_RX_PIN, SCIPH_R307_TX_PIN, SCIPH_R307_BAUD);

WifiNetworkLink network(config);
Esp32HttpTransport transport(config, kHttpTimeoutMs);
DeviceApiClient api(transport, systemClock);

SlotMap slots;
ServerSlotSnapshot serverSlots;
EnrollmentJournalStore journalStore(nvs);
DeletionAttemptStore deletionStore(nvs);

PhysicalSlotScanner scanner(sensor);
SlotReconciler reconciler(api, sensor, slots, serverSlots, logger, reconciliationSettings);
EnrollmentCoordinator enrollment(api, sensor, slots, serverSlots, journalStore, feedback, systemClock, logger,
                                 captureSettings, replaySettings);
IdentificationCoordinator identification(api, sensor, feedback, systemClock, logger, captureSettings, replaySettings);
DeletionCoordinator deletion(api, sensor, slots, deletionStore, hardwareRandom, systemClock, logger, replaySettings,
                             controllerSettings.deletionSensorErrorRetryMs);
OperationScheduler scheduler(schedulerSettings);

ReaderController controller(ReaderDependencies{systemClock, logger, feedback, network, sensor, api, slots, serverSlots,
                                               scanner, reconciler, enrollment, identification, deletion, scheduler},
                            controllerSettings, SCIPH_FIRMWARE_VERSION);

SerialConsole* serialConsole = nullptr;
std::string consoleLine;

void restartReader() {
    Serial.flush();
    delay(200);
    ESP.restart();
}

void readConsole() {
    while (Serial.available() > 0) {
        const char c = static_cast<char>(Serial.read());
        if (c == '\n') {
            serialConsole->handleLine(consoleLine);
            consoleLine.clear();
        } else if (c != '\r' && consoleLine.size() < kMaxConsoleLine) {
            consoleLine.push_back(c);
        }
    }
}

}  // namespace

void setup() {
    Serial.setRxBufferSize(2048);
    Serial.begin(SCIPH_CONSOLE_BAUD);
    delay(200);
    console.println(std::string("SCI-PH lector de huellas ") + SCIPH_FIRMWARE_VERSION);
#if SCIPH_ALLOW_DEV_HTTP
    console.println("AVISO: compilacion de DESARROLLO (permite HTTP sin cifrar). No usar en produccion.");
#endif
#if SCIPH_DEBUG_SLOTS
    console.println("AVISO: diagnostico de slots activo (los logs muestran numeros de slot).");
#endif

    indicator.begin();

    ConfigError error = ConfigError::None;
    const ConfigLoadResult loaded = configs.load(config, developmentHttpBuild(), error);
    provisioned = loaded == ConfigLoadResult::Loaded;
    if (loaded == ConfigLoadResult::Invalid) {
        console.println(std::string("Configuracion guardada no valida: ") + configErrorMessage(error) +
                        ". Escriba 'provision'.");
    }
    if (provisioned && config.transport == TransportMode::DevelopmentHttp) {
        console.println("AVISO: transporte HTTP de desarrollo activo.");
    }

    static ConsoleHooks hooks;
    hooks.statusLines = []() { return controller.statusLines(); };
    hooks.reconcile = [](bool allowLargeDeletion) { controller.requestReconciliation(allowLargeDeletion); };
    hooks.discardUnreadableJournal = []() { return enrollment.discardUnreadableJournal(); };
    hooks.restart = []() { restartReader(); };
    static SerialConsole consoleCommands(console, configs, config, provisioned, developmentHttpBuild(), hooks);
    serialConsole = &consoleCommands;

    sensor.begin();
    controller.begin(provisioned, config.transport == TransportMode::Https);
    console.println("Escriba 'help' para ver los comandos.");
}

void loop() {
    readConsole();
    controller.tick();
    feedback.tick();
    delay(10);
}

#endif
