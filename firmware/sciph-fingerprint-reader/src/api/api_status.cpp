#include "api/api_status.h"

namespace sciph {

ApiStatus classifyHttpStatus(int httpStatus) {
    switch (httpStatus) {
        case 0:
            return ApiStatus::NoResponse;
        case 200:
            return ApiStatus::Ok;
        case 204:
            return ApiStatus::NoContent;
        case 400:
            return ApiStatus::BadRequest;
        case 401:
            return ApiStatus::Unauthorized;
        case 403:
            return ApiStatus::Forbidden;
        case 404:
            return ApiStatus::NotFound;
        case 409:
            return ApiStatus::Conflict;
        case 429:
            return ApiStatus::RateLimited;
        default:
            break;
    }
    if (httpStatus < 0) {
        return ApiStatus::NoResponse;
    }
    return httpStatus >= 500 ? ApiStatus::ServerError : ApiStatus::Unexpected;
}

const char* apiStatusName(ApiStatus status) {
    switch (status) {
        case ApiStatus::Ok:
            return "ok";
        case ApiStatus::NoContent:
            return "no_content";
        case ApiStatus::NoResponse:
            return "no_response";
        case ApiStatus::ServerError:
            return "server_error";
        case ApiStatus::BadRequest:
            return "bad_request";
        case ApiStatus::Unauthorized:
            return "unauthorized";
        case ApiStatus::Forbidden:
            return "missing_scope";
        case ApiStatus::NotFound:
            return "not_found";
        case ApiStatus::Conflict:
            return "conflict";
        case ApiStatus::RateLimited:
            return "rate_limited";
        case ApiStatus::Unexpected:
            return "unexpected_status";
        case ApiStatus::InvalidResponse:
            return "invalid_response";
    }
    return "unknown";
}

bool isDefinitiveRejection(ApiStatus status) {
    return status == ApiStatus::BadRequest || status == ApiStatus::NotFound || status == ApiStatus::Conflict;
}

bool isUncertainResultResponse(ApiStatus status) {
    return status != ApiStatus::NoContent && !isDefinitiveRejection(status) && status != ApiStatus::Unauthorized &&
           status != ApiStatus::Forbidden;
}

}  // namespace sciph
