#include "fingerprint/cleanup/deletion_attempt.h"

#include "core/uuid.h"
#include "storage/record_format.h"

namespace sciph {

namespace {

constexpr const char* kSpace = "sciph-del";
constexpr const char* kKey = "attempt";

}  // namespace

// 1|<deletionId>|<attemptId>|<outcome>
std::string serializeDeletionAttempt(const DeletionAttempt& attempt) {
    return "1|" + attempt.deletionId + "|" + attempt.attemptId + "|" + outcomeName(attempt.outcome);
}

bool parseDeletionAttempt(const std::string& text, DeletionAttempt& attempt) {
    const std::vector<std::string> fields = splitRecord(text, '|');
    DeletionAttempt parsed;
    if (fields.size() != 4 || fields[0] != "1" || !isUuid(fields[1]) || !isUuid(fields[2]) ||
        !parseDeletionOutcome(fields[3], parsed.outcome)) {
        return false;
    }
    parsed.deletionId = fields[1];
    parsed.attemptId = fields[2];
    attempt = parsed;
    return true;
}

bool DeletionAttemptStore::load(DeletionAttempt& attempt) {
    std::string text;
    return store_.get(kSpace, kKey, text) && parseDeletionAttempt(text, attempt);
}

bool DeletionAttemptStore::save(const DeletionAttempt& attempt) {
    return store_.put(kSpace, kKey, serializeDeletionAttempt(attempt));
}

bool DeletionAttemptStore::clear() { return store_.remove(kSpace, kKey); }

}  // namespace sciph
