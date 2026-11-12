// Device API protocol: paths, exact bodies, response parsing, status classification, authentication headers,
// provisioning validation, backoff and time parsing.
#include <unity.h>

#include "../support/fakes.h"
#include "api/api_status.h"
#include "api/device_api_client.h"
#include "api/http_transport.h"
#include "config/device_config.h"
#include "core/time_format.h"
#include "core/uuid.h"
#include "network/backoff.h"

using namespace sciph;
using namespace sciph::testing;

void setUp() {}
void tearDown() {}

static void test_paths_match_the_device_api_contract() {
    TEST_ASSERT_EQUAL_STRING("/api/device/heartbeat", device_paths::kHeartbeat);
    TEST_ASSERT_EQUAL_STRING("/api/device/fingerprint/sessions/pending", device_paths::kPendingSession);
    TEST_ASSERT_EQUAL_STRING("/api/device/fingerprint/enrollments/pending", device_paths::kPendingEnrollment);
    TEST_ASSERT_EQUAL_STRING("/api/device/fingerprint/deletions/pending", device_paths::kPendingDeletion);
    TEST_ASSERT_EQUAL_STRING("/api/device/fingerprint/slots", device_paths::kSlots);
    TEST_ASSERT_EQUAL_STRING(("/api/device/fingerprint/sessions/" + uuid(1) + "/result").c_str(),
                             device_paths::sessionResult(uuid(1)).c_str());
    TEST_ASSERT_EQUAL_STRING(("/api/device/fingerprint/enrollments/" + uuid(2) + "/result").c_str(),
                             device_paths::enrollmentResult(uuid(2)).c_str());
    TEST_ASSERT_EQUAL_STRING(("/api/device/fingerprint/deletions/" + uuid(3) + "/result").c_str(),
                             device_paths::deletionResult(uuid(3)).c_str());
}

static void test_result_bodies_are_exact_and_carry_no_score() {
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"Match\",\"slot\":12}",
                             identificationResultBody(IdentificationOutcome::Match, 12).c_str());
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"NoMatch\"}", identificationResultBody(IdentificationOutcome::NoMatch, 12).c_str());
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"LowQuality\"}",
                             identificationResultBody(IdentificationOutcome::LowQuality, -1).c_str());
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"SensorError\"}",
                             identificationResultBody(IdentificationOutcome::SensorError, -1).c_str());
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"Enrolled\",\"slot\":3}", enrollmentResultBody(EnrollmentOutcome::Enrolled, 3).c_str());
    TEST_ASSERT_EQUAL_STRING("{\"outcome\":\"Failed\"}", enrollmentResultBody(EnrollmentOutcome::Failed, 3).c_str());
    TEST_ASSERT_EQUAL_STRING(("{\"attemptId\":\"" + uuid(9) + "\",\"outcome\":\"Deleted\"}").c_str(),
                             deletionResultBody(uuid(9), DeletionOutcome::Deleted).c_str());
    TEST_ASSERT_EQUAL_STRING("{\"firmwareVersion\":\"0.1.0\",\"uptimeSeconds\":42}", heartbeatBody("0.1.0", 42).c_str());

    const std::string match = identificationResultBody(IdentificationOutcome::Match, 12);
    TEST_ASSERT_FALSE(containsText(match, "score"));
    TEST_ASSERT_FALSE(containsText(match, "Score"));
    TEST_ASSERT_FALSE(containsText(match, "confidence"));
    TEST_ASSERT_FALSE(containsText(match, "deviceId"));
    TEST_ASSERT_FALSE(containsText(match, "template"));
}

static void test_pending_items_are_parsed_and_validated() {
    PendingWorkItem item;
    TEST_ASSERT_TRUE(parsePendingItem("{\"sessionId\":\"" + uuid(4) +
                                          "\",\"expiresAtUtc\":\"2026-10-27T12:01:30.1234567+00:00\"}",
                                      "sessionId", item));
    TEST_ASSERT_EQUAL_STRING(uuid(4).c_str(), item.id.c_str());
    TEST_ASSERT_TRUE(item.hasExpiry);
    TEST_ASSERT_TRUE(item.expiresAtEpoch == 1793102490LL);

    TEST_ASSERT_FALSE(parsePendingItem("{\"sessionId\":\"../../admin\"}", "sessionId", item));
    TEST_ASSERT_FALSE(parsePendingItem("{\"enrollmentId\":42}", "enrollmentId", item));
    TEST_ASSERT_FALSE(parsePendingItem("not json", "sessionId", item));

    PendingDeletion deletion;
    TEST_ASSERT_TRUE(parsePendingDeletion("{\"deletionId\":\"" + uuid(5) + "\",\"slot\":5}", deletion));
    TEST_ASSERT_EQUAL_INT(5, deletion.slot);
    TEST_ASSERT_FALSE(parsePendingDeletion("{\"deletionId\":\"" + uuid(5) + "\",\"slot\":-1}", deletion));
    TEST_ASSERT_FALSE(parsePendingDeletion("{\"deletionId\":\"" + uuid(5) + "\",\"slot\":1.5}", deletion));
}

static void test_slot_inventory_is_all_or_nothing() {
    std::vector<int> slots;
    TEST_ASSERT_TRUE(parseSlotInventory("{\"slots\":[0,1,7,22]}", slots));
    TEST_ASSERT_EQUAL_INT(4, static_cast<int>(slots.size()));
    TEST_ASSERT_TRUE(parseSlotInventory("{\"slots\":[]}", slots));
    TEST_ASSERT_EQUAL_INT(0, static_cast<int>(slots.size()));
    TEST_ASSERT_FALSE(parseSlotInventory("{\"slots\":[1,-2]}", slots));
    TEST_ASSERT_FALSE(parseSlotInventory("{\"slots\":[1,\"2\"]}", slots));
    TEST_ASSERT_FALSE(parseSlotInventory("{\"slots\":null}", slots));
    TEST_ASSERT_FALSE(parseSlotInventory("{}", slots));
    TEST_ASSERT_FALSE(parseSlotInventory("[1,2]", slots));
}

static void test_client_never_answers_with_unconfirmed_inventory() {
    FakeClock clock;
    FakeTransport transport;
    DeviceApiClient api(transport, clock);
    transport.queue(HttpMethod::Get, device_paths::kSlots, respond(204));
    ASSERT_ENUM(ApiStatus::InvalidResponse, api.slotInventory().info.status);
    transport.queue(HttpMethod::Get, device_paths::kSlots, respond(200, "{\"slots\":[1,\"x\"]}"));
    ASSERT_ENUM(ApiStatus::InvalidResponse, api.slotInventory().info.status);
    transport.queue(HttpMethod::Get, device_paths::kSlots, respond(200, "{\"slots\":[3,1]}"));
    const ApiResult<std::vector<int>> ok = api.slotInventory();
    ASSERT_ENUM(ApiStatus::Ok, ok.info.status);
    TEST_ASSERT_TRUE(ok.found);
}

static void test_status_classification_and_uncertainty() {
    ASSERT_ENUM(ApiStatus::NoResponse, classifyHttpStatus(0));
    ASSERT_ENUM(ApiStatus::Ok, classifyHttpStatus(200));
    ASSERT_ENUM(ApiStatus::NoContent, classifyHttpStatus(204));
    ASSERT_ENUM(ApiStatus::BadRequest, classifyHttpStatus(400));
    ASSERT_ENUM(ApiStatus::Unauthorized, classifyHttpStatus(401));
    ASSERT_ENUM(ApiStatus::Forbidden, classifyHttpStatus(403));
    ASSERT_ENUM(ApiStatus::NotFound, classifyHttpStatus(404));
    ASSERT_ENUM(ApiStatus::Conflict, classifyHttpStatus(409));
    ASSERT_ENUM(ApiStatus::RateLimited, classifyHttpStatus(429));
    ASSERT_ENUM(ApiStatus::ServerError, classifyHttpStatus(503));
    ASSERT_ENUM(ApiStatus::Unexpected, classifyHttpStatus(302));

    TEST_ASSERT_TRUE(isUncertainResultResponse(ApiStatus::NoResponse));
    TEST_ASSERT_TRUE(isUncertainResultResponse(ApiStatus::ServerError));
    TEST_ASSERT_TRUE(isUncertainResultResponse(ApiStatus::RateLimited));
    TEST_ASSERT_TRUE(isUncertainResultResponse(ApiStatus::Ok));
    TEST_ASSERT_FALSE(isUncertainResultResponse(ApiStatus::NoContent));
    TEST_ASSERT_FALSE(isUncertainResultResponse(ApiStatus::Conflict));
    TEST_ASSERT_FALSE(isUncertainResultResponse(ApiStatus::Unauthorized));
    TEST_ASSERT_FALSE(isUncertainResultResponse(ApiStatus::Forbidden));
    TEST_ASSERT_TRUE(isDefinitiveRejection(ApiStatus::BadRequest));
    TEST_ASSERT_TRUE(isDefinitiveRejection(ApiStatus::NotFound));
    TEST_ASSERT_TRUE(isDefinitiveRejection(ApiStatus::Conflict));
    TEST_ASSERT_EQUAL_STRING("missing_scope", apiStatusName(ApiStatus::Forbidden));
}

static void test_retry_after_parsing() {
    TEST_ASSERT_EQUAL_INT(17, parseRetryAfter("17"));
    TEST_ASSERT_EQUAL_INT(17, parseRetryAfter(" 17 "));
    TEST_ASSERT_EQUAL_INT(-1, parseRetryAfter(""));
    TEST_ASSERT_EQUAL_INT(-1, parseRetryAfter("Wed, 21 Oct 2026 07:28:00 GMT"));
    TEST_ASSERT_EQUAL_INT(-1, parseRetryAfter("-3"));
}

static void test_device_auth_uses_headers_and_the_device_code() {
    DeviceConfig config;
    config.deviceCode = "ESP32-LAB-01";
    config.deviceKey = "not-a-real-key-0123456789";
    config.apiBaseUrl = "https://sciph.local:7155/";
    const std::vector<HttpHeader> headers = deviceAuthHeaders(config);
    TEST_ASSERT_EQUAL_INT(2, static_cast<int>(headers.size()));
    TEST_ASSERT_EQUAL_STRING("X-Device-Id", headers[0].first.c_str());
    TEST_ASSERT_EQUAL_STRING("ESP32-LAB-01", headers[0].second.c_str());
    TEST_ASSERT_EQUAL_STRING("X-Device-Key", headers[1].first.c_str());

    const std::string url = buildRequestUrl(config.apiBaseUrl, device_paths::kHeartbeat);
    TEST_ASSERT_EQUAL_STRING("https://sciph.local:7155/api/device/heartbeat", url.c_str());
    TEST_ASSERT_FALSE(containsText(url, "?"));
    TEST_ASSERT_FALSE(containsText(url, config.deviceKey));
}

static void test_client_puts_no_secret_in_paths_or_bodies() {
    FakeClock clock;
    FakeTransport transport;
    DeviceApiClient api(transport, clock);
    api.heartbeat("0.1.0", 1);
    api.pendingSession();
    api.submitIdentification(uuid(1), IdentificationOutcome::NoMatch, -1);
    for (const HttpRequest& request : transport.requests) {
        TEST_ASSERT_FALSE(containsText(request.path, "?"));
        TEST_ASSERT_FALSE(containsText(request.body, "Key"));
    }
    // An id that is not a UUID never reaches a URL.
    const size_t before = transport.requests.size();
    ASSERT_ENUM(ApiStatus::BadRequest, api.submitEnrollment("../x", EnrollmentOutcome::Failed, -1).status);
    TEST_ASSERT_EQUAL_INT(static_cast<int>(before), static_cast<int>(transport.requests.size()));
}

static DeviceConfig validHttpsConfig() {
    DeviceConfig config;
    config.wifiSsid = "Hospital-IoT";
    config.wifiPassword = "fake-wifi-pass-123";
    config.apiBaseUrl = "https://192.168.1.20:7155";
    config.deviceCode = "ESP32-LAB-01";
    config.deviceKey = "fake-device-key-0123456789";
    config.caCertificatePem = "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n";
    config.transport = TransportMode::Https;
    return config;
}

static void test_config_validation_and_development_http_guard() {
    DeviceConfig config = validHttpsConfig();
    ASSERT_ENUM(ConfigError::None, validateConfig(config, false));

    DeviceConfig noCa = config;
    noCa.caCertificatePem.clear();
    ASSERT_ENUM(ConfigError::CaCertificate, validateConfig(noCa, false));

    DeviceConfig httpUrlInHttpsMode = config;
    httpUrlInHttpsMode.apiBaseUrl = "http://192.168.1.20:5139";
    ASSERT_ENUM(ConfigError::ApiUrl, validateConfig(httpUrlInHttpsMode, true));

    DeviceConfig development = config;
    development.transport = TransportMode::DevelopmentHttp;
    development.apiBaseUrl = "http://192.168.1.20:5139";
    development.caCertificatePem.clear();
    ASSERT_ENUM(ConfigError::DevelopmentHttpNotAllowed, validateConfig(development, false));
    ASSERT_ENUM(ConfigError::None, validateConfig(development, true));

    DeviceConfig queryUrl = config;
    queryUrl.apiBaseUrl = "https://host/?key=abc";
    ASSERT_ENUM(ConfigError::ApiUrl, validateConfig(queryUrl, false));

    DeviceConfig shortKey = config;
    shortKey.deviceKey = "short";
    ASSERT_ENUM(ConfigError::DeviceKey, validateConfig(shortKey, false));

    DeviceConfig longCode = config;
    longCode.deviceCode = std::string(61, 'A');
    ASSERT_ENUM(ConfigError::DeviceCode, validateConfig(longCode, false));

    TEST_ASSERT_EQUAL_STRING("https://host:7155", normalizeBaseUrl("  https://host:7155// ").c_str());
}

static void test_config_repository_round_trip_and_forget() {
    MemoryStore store;
    ConfigRepository repository(store);
    ConfigError error = ConfigError::None;
    DeviceConfig loaded;
    ASSERT_ENUM(ConfigLoadResult::NotProvisioned, repository.load(loaded, false, error));

    const DeviceConfig config = validHttpsConfig();
    TEST_ASSERT_TRUE(repository.save(config));
    ASSERT_ENUM(ConfigLoadResult::Loaded, repository.load(loaded, false, error));
    TEST_ASSERT_EQUAL_STRING(config.deviceKey.c_str(), loaded.deviceKey.c_str());
    TEST_ASSERT_EQUAL_STRING(config.caCertificatePem.c_str(), loaded.caCertificatePem.c_str());

    // The enrollment journal lives elsewhere and survives forget.
    store.put("sciph-jrnl", "enrollment", "kept");
    repository.forget();
    ASSERT_ENUM(ConfigLoadResult::NotProvisioned, repository.load(loaded, false, error));
    TEST_ASSERT_FALSE(containsText(store.dump(), config.deviceKey));
    TEST_ASSERT_FALSE(containsText(store.dump(), config.wifiPassword));
    TEST_ASSERT_TRUE(store.has("sciph-jrnl", "enrollment"));
}

static void test_config_saved_by_a_development_build_is_rejected_by_production() {
    MemoryStore store;
    ConfigRepository repository(store);
    DeviceConfig development = validHttpsConfig();
    development.transport = TransportMode::DevelopmentHttp;
    development.apiBaseUrl = "http://192.168.1.20:5139";
    TEST_ASSERT_TRUE(repository.save(development));
    DeviceConfig loaded;
    ConfigError error = ConfigError::None;
    ASSERT_ENUM(ConfigLoadResult::Invalid, repository.load(loaded, false, error));
    ASSERT_ENUM(ConfigError::DevelopmentHttpNotAllowed, error);
}

static void test_backoff_sequence() {
    ExponentialBackoff backoff(1000, 30000);
    const uint32_t expected[] = {1000, 2000, 4000, 8000, 16000, 30000, 30000};
    for (uint32_t value : expected) {
        TEST_ASSERT_EQUAL_UINT32(value, backoff.nextDelayMs());
    }
    backoff.reset();
    TEST_ASSERT_EQUAL_UINT32(1000, backoff.nextDelayMs());
}

static void test_uuid_generation_and_validation() {
    SequenceRandom random;
    const std::string first = newUuidV4(random);
    const std::string second = newUuidV4(random);
    TEST_ASSERT_TRUE(isUuid(first));
    TEST_ASSERT_TRUE(first != second);
    TEST_ASSERT_EQUAL_INT('4', first[14]);
    TEST_ASSERT_FALSE(isUuid("6f1c"));
    TEST_ASSERT_FALSE(isUuid("zzzzzzzz-0000-4000-8000-000000000000"));
}

static void test_iso8601_parsing() {
    int64_t epoch = 0;
    TEST_ASSERT_TRUE(parseIso8601("1970-01-01T00:00:00Z", epoch));
    TEST_ASSERT_TRUE(epoch == 0);
    TEST_ASSERT_TRUE(parseIso8601("2026-10-27T12:01:30+00:00", epoch));
    TEST_ASSERT_TRUE(epoch == 1793102490LL);
    TEST_ASSERT_TRUE(parseIso8601("2026-10-27T07:01:30-05:00", epoch));
    TEST_ASSERT_TRUE(epoch == 1793102490LL);
    TEST_ASSERT_FALSE(parseIso8601("2026-10-27 12:01:30", epoch));
    TEST_ASSERT_FALSE(parseIso8601("2026-13-27T12:01:30Z", epoch));
    TEST_ASSERT_FALSE(parseIso8601("", epoch));
}

int main(int, char**) {
    UNITY_BEGIN();
    RUN_TEST(test_paths_match_the_device_api_contract);
    RUN_TEST(test_result_bodies_are_exact_and_carry_no_score);
    RUN_TEST(test_pending_items_are_parsed_and_validated);
    RUN_TEST(test_slot_inventory_is_all_or_nothing);
    RUN_TEST(test_client_never_answers_with_unconfirmed_inventory);
    RUN_TEST(test_status_classification_and_uncertainty);
    RUN_TEST(test_retry_after_parsing);
    RUN_TEST(test_device_auth_uses_headers_and_the_device_code);
    RUN_TEST(test_client_puts_no_secret_in_paths_or_bodies);
    RUN_TEST(test_config_validation_and_development_http_guard);
    RUN_TEST(test_config_repository_round_trip_and_forget);
    RUN_TEST(test_config_saved_by_a_development_build_is_rejected_by_production);
    RUN_TEST(test_backoff_sequence);
    RUN_TEST(test_uuid_generation_and_validation);
    RUN_TEST(test_iso8601_parsing);
    return UNITY_END();
}
