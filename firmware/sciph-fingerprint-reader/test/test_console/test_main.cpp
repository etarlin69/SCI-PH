// Serial provisioning (provision / status / forget) and user feedback. Secrets are never printed back.
#include <unity.h>

#include "../support/fakes.h"
#include "config/device_config.h"
#include "feedback/feedback.h"
#include "provisioning/serial_console.h"

using namespace sciph;
using namespace sciph::testing;

void setUp() {}
void tearDown() {}

namespace {

// Fake values for the tests only.
const char* kWifiPassword = "fake-wifi-pass-XYZ";
const char* kDeviceKey = "fake-device-key-0123456789ABCDEF";

struct Harness {
    CapturingOutput output;
    MemoryStore store;
    ConfigRepository configs{store};
    DeviceConfig current;
    int restarts = 0;
    bool reconcileForced = false;
    int reconciles = 0;

    SerialConsole make(bool provisioned, bool developmentHttpAllowed) {
        ConsoleHooks hooks;
        hooks.statusLines = []() { return std::vector<std::string>{"estado: IDLE"}; };
        hooks.reconcile = [this](bool force) {
            ++reconciles;
            reconcileForced = force;
        };
        hooks.discardUnreadableJournal = []() { return false; };
        hooks.restart = [this]() { ++restarts; };
        return SerialConsole(output, configs, current, provisioned, developmentHttpAllowed, hooks);
    }

    void provision(SerialConsole& console, const std::string& url, bool withCa) {
        console.handleLine("provision");
        console.handleLine("Hospital-IoT");
        console.handleLine(kWifiPassword);
        console.handleLine(url);
        console.handleLine("ESP32-LAB-01");
        console.handleLine(kDeviceKey);
        if (withCa) {
            console.handleLine("-----BEGIN CERTIFICATE-----");
            console.handleLine("MIIBszCCAVmgAwIBAgIUQ");
            console.handleLine("-----END CERTIFICATE-----");
        }
    }
};

}  // namespace

static void test_provision_saves_https_configuration_and_restarts() {
    Harness h;
    SerialConsole console = h.make(false, false);
    h.provision(console, "https://192.168.1.20:7155/", true);
    TEST_ASSERT_TRUE(h.output.contains("Resumen"));
    console.handleLine("save");
    TEST_ASSERT_EQUAL_INT(1, h.restarts);

    DeviceConfig loaded;
    ConfigError error = ConfigError::None;
    ASSERT_ENUM(ConfigLoadResult::Loaded, h.configs.load(loaded, false, error));
    TEST_ASSERT_EQUAL_STRING("https://192.168.1.20:7155", loaded.apiBaseUrl.c_str());
    TEST_ASSERT_EQUAL_STRING(kDeviceKey, loaded.deviceKey.c_str());
    TEST_ASSERT_EQUAL_STRING(kWifiPassword, loaded.wifiPassword.c_str());
    ASSERT_ENUM(TransportMode::Https, loaded.transport);
    TEST_ASSERT_TRUE(containsText(loaded.caCertificatePem, "END CERTIFICATE"));

    TEST_ASSERT_FALSE(h.output.contains(kDeviceKey));
    TEST_ASSERT_FALSE(h.output.contains(kWifiPassword));
}

static void test_production_build_refuses_development_http() {
    Harness h;
    SerialConsole console = h.make(false, false);
    console.handleLine("provision");
    console.handleLine("Hospital-IoT");
    console.handleLine(kWifiPassword);
    console.handleLine("http://192.168.1.20:5139");
    TEST_ASSERT_TRUE(h.output.contains(configErrorMessage(ConfigError::DevelopmentHttpNotAllowed)));
    TEST_ASSERT_TRUE(h.output.contains("URL base del Device API"));
    console.handleLine("cancel");
    DeviceConfig loaded;
    ConfigError error = ConfigError::None;
    ASSERT_ENUM(ConfigLoadResult::NotProvisioned, h.configs.load(loaded, false, error));
}

static void test_development_build_accepts_http_without_ca_and_warns() {
    Harness h;
    SerialConsole console = h.make(false, true);
    h.provision(console, "http://192.168.1.20:5139", false);
    TEST_ASSERT_TRUE(h.output.contains("HTTP sin cifrar"));
    console.handleLine("save");
    DeviceConfig loaded;
    ConfigError error = ConfigError::None;
    ASSERT_ENUM(ConfigLoadResult::Loaded, h.configs.load(loaded, true, error));
    ASSERT_ENUM(TransportMode::DevelopmentHttp, loaded.transport);
}

static void test_invalid_configuration_is_not_saved() {
    Harness h;
    SerialConsole console = h.make(false, false);
    console.handleLine("provision");
    console.handleLine("Hospital-IoT");
    console.handleLine(kWifiPassword);
    console.handleLine("https://192.168.1.20:7155");
    console.handleLine("ESP32-LAB-01");
    console.handleLine("short");
    console.handleLine("-----BEGIN CERTIFICATE-----");
    console.handleLine("-----END CERTIFICATE-----");
    console.handleLine("save");
    TEST_ASSERT_TRUE(h.output.contains("No se guardo"));
    TEST_ASSERT_EQUAL_INT(0, h.restarts);
    TEST_ASSERT_FALSE(h.output.contains("short"));
}

static void test_cancel_discards_the_draft() {
    Harness h;
    SerialConsole console = h.make(false, false);
    console.handleLine("provision");
    console.handleLine("Hospital-IoT");
    console.handleLine("cancel");
    TEST_ASSERT_FALSE(console.provisioningActive());
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(h.store.values.size()));
}

static void test_status_hides_secrets() {
    Harness h;
    h.current.wifiSsid = "Hospital-IoT";
    h.current.wifiPassword = kWifiPassword;
    h.current.apiBaseUrl = "https://192.168.1.20:7155";
    h.current.deviceCode = "ESP32-LAB-01";
    h.current.deviceKey = kDeviceKey;
    SerialConsole console = h.make(true, false);
    console.handleLine("status");
    TEST_ASSERT_TRUE(h.output.contains("ESP32-LAB-01"));
    TEST_ASSERT_TRUE(h.output.contains("clave del dispositivo: configurada"));
    TEST_ASSERT_TRUE(h.output.contains("estado: IDLE"));
    TEST_ASSERT_FALSE(h.output.contains(kDeviceKey));
    TEST_ASSERT_FALSE(h.output.contains(kWifiPassword));
}

static void test_forget_requires_confirmation_and_keeps_the_journal() {
    Harness h;
    SerialConsole console = h.make(false, false);
    h.provision(console, "https://192.168.1.20:7155", true);
    console.handleLine("save");
    h.store.put("sciph-jrnl", "enrollment", "journal");
    h.store.put("sciph-del", "attempt", "attempt");

    console.handleLine("forget");
    console.handleLine("yes");
    DeviceConfig loaded;
    ConfigError error = ConfigError::None;
    ASSERT_ENUM(ConfigLoadResult::Loaded, h.configs.load(loaded, false, error));

    console.handleLine("forget");
    console.handleLine("FORGET");
    ASSERT_ENUM(ConfigLoadResult::NotProvisioned, h.configs.load(loaded, false, error));
    TEST_ASSERT_FALSE(containsText(h.store.dump(), kDeviceKey));
    TEST_ASSERT_FALSE(containsText(h.store.dump(), kWifiPassword));
    TEST_ASSERT_TRUE(h.store.has("sciph-jrnl", "enrollment"));
    TEST_ASSERT_TRUE(h.store.has("sciph-del", "attempt"));
    TEST_ASSERT_TRUE(h.output.contains("plantillas del sensor"));
    TEST_ASSERT_EQUAL_INT(2, h.restarts);
}

static void test_reconcile_commands() {
    Harness h;
    SerialConsole console = h.make(true, false);
    console.handleLine("reconcile");
    TEST_ASSERT_EQUAL_INT(1, h.reconciles);
    TEST_ASSERT_FALSE(h.reconcileForced);
    console.handleLine("reconcile force");
    TEST_ASSERT_EQUAL_INT(2, h.reconciles);
    TEST_ASSERT_TRUE(h.reconcileForced);
    console.handleLine("journal discard");
    TEST_ASSERT_TRUE(h.output.contains("No hay un registro ilegible"));
}

static void test_feedback_messages_and_led_modes() {
    FakeClock clock;
    CapturingOutput output;
    FeedbackController feedback(output, nullptr, clock);
    feedback.signal(FeedbackSignal::PlaceFinger);
    feedback.signal(FeedbackSignal::RemoveFinger);
    feedback.signal(FeedbackSignal::PlaceAgain);
    feedback.signal(FeedbackSignal::NotRecognized);
    feedback.signal(FeedbackSignal::AuthenticationFailed);
    TEST_ASSERT_TRUE(output.contains("Coloque el dedo"));
    TEST_ASSERT_TRUE(output.contains("Retire el dedo"));
    TEST_ASSERT_TRUE(output.contains("mismo dedo otra vez"));
    TEST_ASSERT_TRUE(output.contains("no reconocida"));
    ASSERT_ENUM(LedMode::On, feedback.ledMode());
    ASSERT_ENUM(LedMode::BlinkSlow, feedbackLedMode(FeedbackSignal::NetworkError));
    ASSERT_ENUM(LedMode::BlinkFast, feedbackLedMode(FeedbackSignal::SensorError));
    ASSERT_ENUM(LedMode::Off, feedbackLedMode(FeedbackSignal::Ready));
}

int main(int, char**) {
    UNITY_BEGIN();
    RUN_TEST(test_provision_saves_https_configuration_and_restarts);
    RUN_TEST(test_production_build_refuses_development_http);
    RUN_TEST(test_development_build_accepts_http_without_ca_and_warns);
    RUN_TEST(test_invalid_configuration_is_not_saved);
    RUN_TEST(test_cancel_discards_the_draft);
    RUN_TEST(test_status_hides_secrets);
    RUN_TEST(test_forget_requires_confirmation_and_keeps_the_journal);
    RUN_TEST(test_reconcile_commands);
    RUN_TEST(test_feedback_messages_and_led_modes);
    return UNITY_END();
}
