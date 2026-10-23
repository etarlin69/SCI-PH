using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SCI_PH.Application.Common.Idempotency
{
    // Deterministic SHA-256 (lowercase hex, 64 chars) of the logical payload of a request: the caller passes the
    // already normalized values in a fixed order, never the raw JSON, tokens, server times or user metadata.
    // Same key + same hash replays the first result; same key + another hash is a conflict.
    public static class IdempotencyRequestHash
    {
        public const int Length = 64;

        public static string Compute(params string?[] logicalValues)
        {
            // A JSON array keeps the values unambiguous (no separator can be forged inside a value).
            var canonical = JsonSerializer.SerializeToUtf8Bytes(logicalValues);
            return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
        }

        // UTC, fixed format and full precision, so 10:00-04:00 and 14:00Z are the same instant and the same hash.
        public static string Instant(DateTimeOffset value) =>
            value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture);

        public static string Text(string? value) => value?.Trim() ?? string.Empty;
    }
}
