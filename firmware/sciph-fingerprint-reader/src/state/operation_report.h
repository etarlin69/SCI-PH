#pragma once

#include "api/api_status.h"

namespace sciph {

// What one scheduled operation tells the controller: the API answer that matters (401/403/429/no response are
// handled centrally) and whether the sensor misbehaved (a health check follows).
struct OperationReport {
    bool apiCalled = false;
    ApiResponseInfo api;
    bool sensorFault = false;
    // An interrupted enrollment was resolved: the contract asks for a reconciliation afterwards.
    bool journalResolved = false;

    void record(const ApiResponseInfo& info) {
        apiCalled = true;
        api = info;
    }
};

}  // namespace sciph
