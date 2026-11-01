#include "api/device_api_client.h"

#include <ArduinoJson.h>

#include "core/time_format.h"
#include "core/uuid.h"

namespace sciph {

namespace device_paths {

std::string sessionResult(const std::string& sessionId) {
    return "/api/device/fingerprint/sessions/" + sessionId + "/result";
}

std::string enrollmentResult(const std::string& enrollmentId) {
    return "/api/device/fingerprint/enrollments/" + enrollmentId + "/result";
}

std::string deletionResult(const std::string& deletionId) {
    return "/api/device/fingerprint/deletions/" + deletionId + "/result";
}

}  // namespace device_paths

const char* outcomeName(IdentificationOutcome outcome) {
    switch (outcome) {
        case IdentificationOutcome::Match:
            return "Match";
        case IdentificationOutcome::NoMatch:
            return "NoMatch";
        case IdentificationOutcome::LowQuality:
            return "LowQuality";
        case IdentificationOutcome::SensorError:
            return "SensorError";
    }
    return "SensorError";
}

const char* outcomeName(EnrollmentOutcome outcome) {
    switch (outcome) {
        case EnrollmentOutcome::Enrolled:
            return "Enrolled";
        case EnrollmentOutcome::Failed:
            return "Failed";
        case EnrollmentOutcome::LowQuality:
            return "LowQuality";
        case EnrollmentOutcome::SensorError:
            return "SensorError";
    }
    return "SensorError";
}

const char* outcomeName(DeletionOutcome outcome) {
    switch (outcome) {
        case DeletionOutcome::Deleted:
            return "Deleted";
        case DeletionOutcome::NotFound:
            return "NotFound";
        case DeletionOutcome::SensorError:
            return "SensorError";
    }
    return "SensorError";
}

bool parseDeletionOutcome(const std::string& name, DeletionOutcome& outcome) {
    if (name == "Deleted") {
        outcome = DeletionOutcome::Deleted;
    } else if (name == "NotFound") {
        outcome = DeletionOutcome::NotFound;
    } else if (name == "SensorError") {
        outcome = DeletionOutcome::SensorError;
    } else {
        return false;
    }
    return true;
}

std::string identificationResultBody(IdentificationOutcome outcome, int slot) {
    JsonDocument document;
    document["outcome"] = outcomeName(outcome);
    if (outcome == IdentificationOutcome::Match) {
        document["slot"] = slot;
    }
    std::string body;
    serializeJson(document, body);
    return body;
}

std::string enrollmentResultBody(EnrollmentOutcome outcome, int slot) {
    JsonDocument document;
    document["outcome"] = outcomeName(outcome);
    if (outcome == EnrollmentOutcome::Enrolled) {
        document["slot"] = slot;
    }
    std::string body;
    serializeJson(document, body);
    return body;
}

std::string deletionResultBody(const std::string& attemptId, DeletionOutcome outcome) {
    JsonDocument document;
    document["attemptId"] = attemptId;
    document["outcome"] = outcomeName(outcome);
    std::string body;
    serializeJson(document, body);
    return body;
}

std::string heartbeatBody(const char* firmwareVersion, uint32_t uptimeSeconds) {
    JsonDocument document;
    document["firmwareVersion"] = firmwareVersion;
    document["uptimeSeconds"] = uptimeSeconds;
    std::string body;
    serializeJson(document, body);
    return body;
}

bool parsePendingItem(const std::string& body, const char* idField, PendingWorkItem& item) {
    JsonDocument document;
    if (deserializeJson(document, body)) {
        return false;
    }
    const JsonDocument& view = document;
    JsonVariantConst id = view[idField];
    if (!id.is<const char*>()) {
        return false;
    }
    PendingWorkItem parsed;
    parsed.id = id.as<const char*>();
    if (!isUuid(parsed.id)) {
        return false;
    }
    JsonVariantConst expires = view["expiresAtUtc"];
    parsed.hasExpiry = expires.is<const char*>() && parseIso8601(expires.as<const char*>(), parsed.expiresAtEpoch);
    item = parsed;
    return true;
}

bool parsePendingDeletion(const std::string& body, PendingDeletion& deletion) {
    JsonDocument document;
    if (deserializeJson(document, body)) {
        return false;
    }
    const JsonDocument& view = document;
    JsonVariantConst id = view["deletionId"];
    JsonVariantConst slot = view["slot"];
    if (!id.is<const char*>() || !slot.is<int>()) {
        return false;
    }
    PendingDeletion parsed;
    parsed.deletionId = id.as<const char*>();
    parsed.slot = slot.as<int>();
    if (!isUuid(parsed.deletionId) || parsed.slot < 0) {
        return false;
    }
    deletion = parsed;
    return true;
}

bool parseSlotInventory(const std::string& body, std::vector<int>& slots) {
    JsonDocument document;
    if (deserializeJson(document, body)) {
        return false;
    }
    const JsonDocument& view = document;
    JsonVariantConst list = view["slots"];
    if (!list.is<JsonArrayConst>()) {
        return false;
    }
    std::vector<int> parsed;
    for (JsonVariantConst item : list.as<JsonArrayConst>()) {
        if (!item.is<int>() || item.as<int>() < 0) {
            return false;
        }
        parsed.push_back(item.as<int>());
    }
    slots = parsed;
    return true;
}

ApiResponseInfo DeviceApiClient::exchange(HttpMethod method, const std::string& path, const std::string& body,
                                          std::string* responseBody) {
    HttpRequest request;
    request.method = method;
    request.path = path;
    request.body = body;

    const uint64_t started = clock_.monotonicMs();
    const HttpResponse response = transport_.send(request);
    ApiResponseInfo info;
    info.httpStatus = response.status;
    info.status = classifyHttpStatus(response.status);
    info.retryAfterSeconds = response.retryAfterSeconds;
    info.latencyMs = static_cast<uint32_t>(clock_.monotonicMs() - started);
    if (responseBody != nullptr) {
        *responseBody = response.body;
    }
    return info;
}

ApiResult<PendingWorkItem> DeviceApiClient::pendingSession() {
    ApiResult<PendingWorkItem> result;
    std::string body;
    result.info = exchange(HttpMethod::Get, device_paths::kPendingSession, "", &body);
    if (result.info.status == ApiStatus::Ok) {
        result.found = parsePendingItem(body, "sessionId", result.value);
        if (!result.found) {
            result.info.status = ApiStatus::InvalidResponse;
        }
    }
    return result;
}

ApiResult<PendingWorkItem> DeviceApiClient::pendingEnrollment() {
    ApiResult<PendingWorkItem> result;
    std::string body;
    result.info = exchange(HttpMethod::Get, device_paths::kPendingEnrollment, "", &body);
    if (result.info.status == ApiStatus::Ok) {
        result.found = parsePendingItem(body, "enrollmentId", result.value);
        if (!result.found) {
            result.info.status = ApiStatus::InvalidResponse;
        }
    }
    return result;
}

ApiResult<PendingDeletion> DeviceApiClient::pendingDeletion() {
    ApiResult<PendingDeletion> result;
    std::string body;
    result.info = exchange(HttpMethod::Get, device_paths::kPendingDeletion, "", &body);
    if (result.info.status == ApiStatus::Ok) {
        result.found = parsePendingDeletion(body, result.value);
        if (!result.found) {
            result.info.status = ApiStatus::InvalidResponse;
        }
    }
    return result;
}

ApiResult<std::vector<int>> DeviceApiClient::slotInventory() {
    ApiResult<std::vector<int>> result;
    std::string body;
    result.info = exchange(HttpMethod::Get, device_paths::kSlots, "", &body);
    if (result.info.status == ApiStatus::Ok) {
        result.found = parseSlotInventory(body, result.value);
        if (!result.found) {
            result.info.status = ApiStatus::InvalidResponse;
        }
    } else if (result.info.status == ApiStatus::NoContent) {
        // The contract never answers 204 here: an inventory that cannot be confirmed.
        result.info.status = ApiStatus::InvalidResponse;
    }
    return result;
}

ApiResponseInfo DeviceApiClient::submitIdentification(const std::string& sessionId, IdentificationOutcome outcome,
                                                      int slot) {
    if (!isUuid(sessionId)) {
        ApiResponseInfo invalid;
        invalid.status = ApiStatus::BadRequest;
        return invalid;
    }
    return exchange(HttpMethod::Post, device_paths::sessionResult(sessionId), identificationResultBody(outcome, slot),
                    nullptr);
}

ApiResponseInfo DeviceApiClient::submitEnrollment(const std::string& enrollmentId, EnrollmentOutcome outcome, int slot) {
    if (!isUuid(enrollmentId)) {
        ApiResponseInfo invalid;
        invalid.status = ApiStatus::BadRequest;
        return invalid;
    }
    return exchange(HttpMethod::Post, device_paths::enrollmentResult(enrollmentId), enrollmentResultBody(outcome, slot),
                    nullptr);
}

ApiResponseInfo DeviceApiClient::submitDeletion(const std::string& deletionId, const std::string& attemptId,
                                                DeletionOutcome outcome) {
    if (!isUuid(deletionId) || !isUuid(attemptId)) {
        ApiResponseInfo invalid;
        invalid.status = ApiStatus::BadRequest;
        return invalid;
    }
    return exchange(HttpMethod::Post, device_paths::deletionResult(deletionId), deletionResultBody(attemptId, outcome),
                    nullptr);
}

ApiResponseInfo DeviceApiClient::heartbeat(const char* firmwareVersion, uint32_t uptimeSeconds) {
    return exchange(HttpMethod::Post, device_paths::kHeartbeat, heartbeatBody(firmwareVersion, uptimeSeconds), nullptr);
}

}  // namespace sciph
