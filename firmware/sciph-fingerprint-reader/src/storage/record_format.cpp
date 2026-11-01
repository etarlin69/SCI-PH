#include "storage/record_format.h"

namespace sciph {

std::vector<std::string> splitRecord(const std::string& text, char separator) {
    std::vector<std::string> fields;
    size_t start = 0;
    while (true) {
        const size_t end = text.find(separator, start);
        if (end == std::string::npos) {
            fields.push_back(text.substr(start));
            return fields;
        }
        fields.push_back(text.substr(start, end - start));
        start = end + 1;
    }
}

bool parseInteger(const std::string& text, int64_t& value) {
    if (text.empty() || text.size() > 19) {
        return false;
    }
    size_t position = 0;
    bool negative = false;
    if (text[0] == '-') {
        negative = true;
        position = 1;
        if (text.size() == 1) {
            return false;
        }
    }
    int64_t parsed = 0;
    for (; position < text.size(); ++position) {
        if (text[position] < '0' || text[position] > '9') {
            return false;
        }
        parsed = parsed * 10 + (text[position] - '0');
    }
    value = negative ? -parsed : parsed;
    return true;
}

}  // namespace sciph
