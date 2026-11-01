#pragma once

#include <cstdint>
#include <string>

#include "storage/key_value_store.h"

namespace sciph {

enum class JournalState : uint8_t {
    // Written BEFORE the physical Store: the slot may or may not hold the new template.
    Storing = 1,
    // The template is in the slot; the Enrolled result has not been accepted (204) yet.
    Stored = 2,
    // The server will not accept it (400/404/409, or 403 before any uncertain delivery): delete the slot, then clear.
    Compensating = 3
};

const char* journalStateName(JournalState state);

// The one enrollment in flight, persisted in NVS as a single value so a power cut never leaves half a record.
struct EnrollmentJournal {
    std::string enrollmentId;
    int slot = -1;
    JournalState state = JournalState::Storing;
    bool hasExpiry = false;
    int64_t expiresAtEpoch = 0;
    // A submission may have reached the server (no response, timeout, 5xx, 429): a later 403 must not delete the slot,
    // because the earlier submission may have created the reference.
    bool uncertainDelivery = false;
};

std::string serializeJournal(const EnrollmentJournal& journal);
bool parseJournal(const std::string& text, EnrollmentJournal& journal);

enum class JournalLoad { None, Loaded, Unreadable };

class EnrollmentJournalStore {
public:
    explicit EnrollmentJournalStore(IKeyValueStore& store) : store_(store) {}
    JournalLoad load(EnrollmentJournal& journal);
    bool save(const EnrollmentJournal& journal);
    bool clear();

private:
    IKeyValueStore& store_;
};

}  // namespace sciph
