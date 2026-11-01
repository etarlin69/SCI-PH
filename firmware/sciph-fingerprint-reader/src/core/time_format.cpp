#include "core/time_format.h"

namespace sciph {

namespace {

bool readDigits(const std::string& text, size_t position, size_t count, int& value) {
    if (position + count > text.size()) {
        return false;
    }
    value = 0;
    for (size_t i = position; i < position + count; ++i) {
        if (text[i] < '0' || text[i] > '9') {
            return false;
        }
        value = value * 10 + (text[i] - '0');
    }
    return true;
}

// Days since 1970-01-01 of a proleptic Gregorian date (H. Hinnant's days_from_civil).
int64_t daysFromCivil(int year, int month, int day) {
    year -= month <= 2 ? 1 : 0;
    const int64_t era = (year >= 0 ? year : year - 399) / 400;
    const int64_t yearOfEra = year - era * 400;
    const int64_t dayOfYear = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1;
    const int64_t dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear;
    return era * 146097 + dayOfEra - 719468;
}

}  // namespace

bool parseIso8601(const std::string& text, int64_t& epochSeconds) {
    int year = 0;
    int month = 0;
    int day = 0;
    int hour = 0;
    int minute = 0;
    int second = 0;
    if (!readDigits(text, 0, 4, year) || text.size() < 19 || text[4] != '-' || !readDigits(text, 5, 2, month) ||
        text[7] != '-' || !readDigits(text, 8, 2, day) || (text[10] != 'T' && text[10] != 't') ||
        !readDigits(text, 11, 2, hour) || text[13] != ':' || !readDigits(text, 14, 2, minute) || text[16] != ':' ||
        !readDigits(text, 17, 2, second)) {
        return false;
    }
    if (month < 1 || month > 12 || day < 1 || day > 31 || hour > 23 || minute > 59 || second > 60) {
        return false;
    }

    size_t position = 19;
    if (position < text.size() && text[position] == '.') {
        const size_t start = ++position;
        while (position < text.size() && text[position] >= '0' && text[position] <= '9') {
            ++position;
        }
        if (position == start) {
            return false;
        }
    }

    if (position >= text.size()) {
        return false;
    }
    int offsetSeconds = 0;
    if (text[position] == 'Z' || text[position] == 'z') {
        ++position;
    } else if (text[position] == '+' || text[position] == '-') {
        const int sign = text[position] == '-' ? -1 : 1;
        int offsetHours = 0;
        int offsetMinutes = 0;
        if (!readDigits(text, position + 1, 2, offsetHours) || position + 3 >= text.size() ||
            text[position + 3] != ':' || !readDigits(text, position + 4, 2, offsetMinutes) || offsetHours > 23 ||
            offsetMinutes > 59) {
            return false;
        }
        offsetSeconds = sign * (offsetHours * 3600 + offsetMinutes * 60);
        position += 6;
    } else {
        return false;
    }
    if (position != text.size()) {
        return false;
    }

    epochSeconds = daysFromCivil(year, month, day) * 86400 + hour * 3600 + minute * 60 + (second > 59 ? 59 : second) -
                   offsetSeconds;
    return true;
}

}  // namespace sciph
