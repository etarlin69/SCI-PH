#pragma once

#include <string>

#include "api/device_api_client.h"
#include "storage/key_value_store.h"

namespace sciph {

// One physical deletion attempt whose result has not been accepted yet. Persisted so that, after a reboot, the SAME
// attemptId and outcome are sent again instead of counting a second attempt.
struct DeletionAttempt {
    std::string deletionId;
    std::string attemptId;
    DeletionOutcome outcome = DeletionOutcome::SensorError;
};

std::string serializeDeletionAttempt(const DeletionAttempt& attempt);
bool parseDeletionAttempt(const std::string& text, DeletionAttempt& attempt);

class DeletionAttemptStore {
public:
    explicit DeletionAttemptStore(IKeyValueStore& store) : store_(store) {}
    bool load(DeletionAttempt& attempt);
    bool save(const DeletionAttempt& attempt);
    bool clear();

private:
    IKeyValueStore& store_;
};

}  // namespace sciph
