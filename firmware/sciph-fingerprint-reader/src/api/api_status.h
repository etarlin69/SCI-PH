#pragma once

#include <cstdint>

namespace sciph {

enum class ApiStatus {
    Ok,               // 200
    NoContent,        // 204
    NoResponse,       // transport failure: DNS, TLS, reset, timeout
    ServerError,      // 5xx
    BadRequest,       // 400
    Unauthorized,     // 401: missing, invalid or revoked credential, or no HTTPS
    Forbidden,        // 403: the credential lacks the scope
    NotFound,         // 404
    Conflict,         // 409
    RateLimited,      // 429
    Unexpected,       // any other status
    InvalidResponse   // 200 with a body that does not follow the contract
};

ApiStatus classifyHttpStatus(int httpStatus);
const char* apiStatusName(ApiStatus status);

// A result POST whose fate is unknown: the server may or may not have applied it, so the SAME result is sent again.
// Everything except 204 (accepted), 400/404/409 (definitively not applied) and 401/403 (not processed).
bool isUncertainResultResponse(ApiStatus status);

// 400, 404, 409: the server did not and will not apply this result.
bool isDefinitiveRejection(ApiStatus status);

struct ApiResponseInfo {
    ApiStatus status = ApiStatus::NoResponse;
    int httpStatus = 0;
    int retryAfterSeconds = -1;
    uint32_t latencyMs = 0;
};

}  // namespace sciph
