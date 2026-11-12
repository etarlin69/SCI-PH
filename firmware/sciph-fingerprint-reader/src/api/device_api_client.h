#pragma once

#include <cstdint>
#include <string>
#include <vector>

#include "api/api_status.h"
#include "api/http_transport.h"
#include "core/clock.h"

namespace sciph {

// The existing Device API, exactly as documented in docs/api/device-api-contracts.md.
namespace device_paths {
constexpr const char* kHeartbeat = "/api/device/heartbeat";
constexpr const char* kPendingSession = "/api/device/fingerprint/sessions/pending";
constexpr const char* kPendingEnrollment = "/api/device/fingerprint/enrollments/pending";
constexpr const char* kPendingDeletion = "/api/device/fingerprint/deletions/pending";
constexpr const char* kSlots = "/api/device/fingerprint/slots";
std::string sessionResult(const std::string& sessionId);
std::string enrollmentResult(const std::string& enrollmentId);
std::string deletionResult(const std::string& deletionId);
}  // namespace device_paths

enum class IdentificationOutcome { Match, NoMatch, LowQuality, SensorError };
enum class EnrollmentOutcome { Enrolled, Failed, LowQuality, SensorError };
enum class DeletionOutcome { Deleted, NotFound, SensorError };

const char* outcomeName(IdentificationOutcome outcome);
const char* outcomeName(EnrollmentOutcome outcome);
const char* outcomeName(DeletionOutcome outcome);
bool parseDeletionOutcome(const std::string& name, DeletionOutcome& outcome);

// A pending session or enrollment: only its id and expiry ever reach the reader.
struct PendingWorkItem {
    std::string id;
    bool hasExpiry = false;
    int64_t expiresAtEpoch = 0;
};

struct PendingDeletion {
    std::string deletionId;
    int slot = -1;
};

template <typename T>
struct ApiResult {
    ApiResponseInfo info;
    // 200 with a valid body (always true for the slot inventory on success).
    bool found = false;
    T value{};
};

// Exact request bodies: {"outcome":"Match","slot":12}, {"outcome":"Enrolled","slot":3},
// {"attemptId":"…","outcome":"Deleted"}, {"firmwareVersion":"…","uptimeSeconds":0}. No score, template or deviceId.
std::string identificationResultBody(IdentificationOutcome outcome, int slot);
std::string enrollmentResultBody(EnrollmentOutcome outcome, int slot);
std::string deletionResultBody(const std::string& attemptId, DeletionOutcome outcome);
std::string heartbeatBody(const char* firmwareVersion, uint32_t uptimeSeconds);

bool parsePendingItem(const std::string& body, const char* idField, PendingWorkItem& item);
bool parsePendingDeletion(const std::string& body, PendingDeletion& deletion);
// {"slots":[...]}: every element a non-negative integer, or the whole inventory is rejected.
bool parseSlotInventory(const std::string& body, std::vector<int>& slots);

class DeviceApiClient {
public:
    DeviceApiClient(IHttpTransport& transport, IClock& clock) : transport_(transport), clock_(clock) {}

    ApiResult<PendingWorkItem> pendingSession();
    ApiResult<PendingWorkItem> pendingEnrollment();
    ApiResult<PendingDeletion> pendingDeletion();
    ApiResult<std::vector<int>> slotInventory();

    ApiResponseInfo submitIdentification(const std::string& sessionId, IdentificationOutcome outcome, int slot);
    ApiResponseInfo submitEnrollment(const std::string& enrollmentId, EnrollmentOutcome outcome, int slot);
    ApiResponseInfo submitDeletion(const std::string& deletionId, const std::string& attemptId, DeletionOutcome outcome);
    ApiResponseInfo heartbeat(const char* firmwareVersion, uint32_t uptimeSeconds);

private:
    ApiResponseInfo exchange(HttpMethod method, const std::string& path, const std::string& body, std::string* responseBody);

    IHttpTransport& transport_;
    IClock& clock_;
};

}  // namespace sciph
