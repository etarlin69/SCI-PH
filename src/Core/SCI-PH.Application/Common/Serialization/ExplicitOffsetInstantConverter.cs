using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SCI_PH.Application.Common.Serialization
{
    // An instant on the wire: ISO-8601 with "Z" or an explicit offset ("2026-10-23T15:30:00Z",
    // "2026-10-23T11:30:00.123-04:00"), returned in UTC. A value without offset is ambiguous and is rejected (400):
    // it is never read in the server's time zone nor assumed to be UTC. Applied per property on contracts that need it.
    public sealed class ExplicitOffsetInstantConverter : JsonConverter<DateTimeOffset>
    {
        private const string Invalid = "The value must be an ISO-8601 timestamp with 'Z' or an explicit offset.";

        private static readonly string[] UtcFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];
        private static readonly string[] OffsetFormats = ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (DateTimeOffset.TryParseExact(text, UtcFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)
                || DateTimeOffset.TryParseExact(text, OffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out utc))
            {
                return utc.ToUniversalTime();
            }

            throw new JsonException(Invalid);
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
    }
}
