#include "storage/journal/enrollment_journal.h"

#include "core/uuid.h"
#include "storage/record_format.h"

namespace sciph {

namespace {

constexpr const char* kSpace = "sciph-jrnl";
constexpr const char* kKey = "enrollment";

}  // namespace

const char* journalStateName(JournalState state) {
    switch (state) {
        case JournalState::Storing:
            return "storing";
        case JournalState::Stored:
            return "stored";
        case JournalState::Compensating:
            return "compensating";
    }
    return "unknown";
}

// 1|<enrollmentId>|<slot>|<state>|<expiresAtEpoch or ->|<uncertainDelivery 0/1>
std::string serializeJournal(const EnrollmentJournal& journal) {
    return "1|" + journal.enrollmentId + "|" + std::to_string(journal.slot) + "|" +
           std::to_string(static_cast<int>(journal.state)) + "|" +
           (journal.hasExpiry ? std::to_string(journal.expiresAtEpoch) : std::string("-")) + "|" +
           (journal.uncertainDelivery ? "1" : "0");
}

bool parseJournal(const std::string& text, EnrollmentJournal& journal) {
    const std::vector<std::string> fields = splitRecord(text, '|');
    if (fields.size() != 6 || fields[0] != "1" || !isUuid(fields[1])) {
        return false;
    }
    int64_t slot = 0;
    int64_t state = 0;
    if (!parseInteger(fields[2], slot) || slot < 0 || slot > 65535 || !parseInteger(fields[3], state) || state < 1 ||
        state > 3 || (fields[5] != "0" && fields[5] != "1")) {
        return false;
    }
    EnrollmentJournal parsed;
    parsed.enrollmentId = fields[1];
    parsed.slot = static_cast<int>(slot);
    parsed.state = static_cast<JournalState>(state);
    if (fields[4] != "-") {
        if (!parseInteger(fields[4], parsed.expiresAtEpoch)) {
            return false;
        }
        parsed.hasExpiry = true;
    }
    parsed.uncertainDelivery = fields[5] == "1";
    journal = parsed;
    return true;
}

JournalLoad EnrollmentJournalStore::load(EnrollmentJournal& journal) {
    std::string text;
    if (!store_.get(kSpace, kKey, text) || text.empty()) {
        return JournalLoad::None;
    }
    return parseJournal(text, journal) ? JournalLoad::Loaded : JournalLoad::Unreadable;
}

bool EnrollmentJournalStore::save(const EnrollmentJournal& journal) {
    return store_.put(kSpace, kKey, serializeJournal(journal));
}

bool EnrollmentJournalStore::clear() { return store_.remove(kSpace, kKey); }

}  // namespace sciph
