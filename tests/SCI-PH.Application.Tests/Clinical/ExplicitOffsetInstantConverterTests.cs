using System.Text.Json;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.DTOs.Clinical;

namespace SCI_PH.Application.Tests.Clinical
{
    // startedAt of OpenPrehospitalEmergencyRequest as the JSON boundary reads it: an instant with "Z" or an explicit
    // offset, normalized to UTC; anything ambiguous or malformed is a JsonException (400 at the HTTP boundary).
    public class ExplicitOffsetInstantConverterTests
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
        private static readonly DateTimeOffset At1530Utc = new(2026, 10, 23, 15, 30, 0, TimeSpan.Zero);

        private static OpenPrehospitalEmergencyRequest Read(string startedAtJson) =>
            JsonSerializer.Deserialize<OpenPrehospitalEmergencyRequest>(
                $$"""{"clientRequestId":"{{Guid.NewGuid()}}","startedAt":{{startedAtJson}},"chiefComplaint":"Dolor"}""", Web)!;

        [Theory]
        [InlineData("2026-10-23T15:30:00Z", 0)]
        [InlineData("2026-10-23T15:30:00.123Z", 123)]
        [InlineData("2026-10-23T11:30:00-04:00", 0)]
        [InlineData("2026-10-23T11:30:00.123-04:00", 123)]
        [InlineData("2026-10-23T15:30:00+00:00", 0)]
        [InlineData("2026-10-24T01:00:00+09:30", 0)]
        [InlineData("2026-10-23T11:30:00-0400", 0)]
        public void AnInstantWithZOrAnOffsetIsReadAsUtc(string startedAt, int milliseconds)
        {
            var value = Read($"\"{startedAt}\"").StartedAt;

            Assert.Equal(At1530Utc.AddMilliseconds(milliseconds), value);
            Assert.Equal(TimeSpan.Zero, value.Offset);
        }

        [Theory]
        [InlineData("\"2026-10-23T15:30:00\"")]
        [InlineData("\"2026-10-23T15:30:00.123\"")]
        [InlineData("\"2026-10-23\"")]
        [InlineData("\"not a date\"")]
        [InlineData("\"\"")]
        [InlineData("\"2026-02-30T10:00:00Z\"")]
        [InlineData("\"2026-10-23T25:30:00Z\"")]
        [InlineData("\"2026-10-23T15:30:00+25:00\"")]
        [InlineData("\"2026-10-23T15:30:00z\"")]
        [InlineData("\"2026-10-23 15:30:00Z\"")]
        [InlineData("1761233400")]
        [InlineData("null")]
        public void AnAmbiguousOrMalformedInstantIsRejected(string startedAtJson)
        {
            Assert.Throws<JsonException>(() => Read(startedAtJson));
        }

        [Fact]
        public void TheSameInstantWithAnotherOffsetHasTheSameRequestHash()
        {
            var z = Read("\"2026-10-23T15:30:00Z\"").StartedAt;
            var offset = Read("\"2026-10-23T11:30:00-04:00\"").StartedAt;

            Assert.Equal(z, offset);
            Assert.Equal(
                IdempotencyRequestHash.Compute(IdempotencyRequestHash.Instant(z), "Dolor"),
                IdempotencyRequestHash.Compute(IdempotencyRequestHash.Instant(offset), "Dolor"));
        }
    }
}
