using System.Security.Cryptography;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Application.Tests.Clinical
{
    public sealed class PatientQrTokenTests
    {
        [Fact]
        public void AGeneratedTokenHas256RandomBitsInTheCanonicalFormat()
        {
            var (payload, hash) = PatientQrToken.Generate();

            Assert.Matches("^sciphqr:v1:[A-Za-z0-9_-]{43}$", payload);
            Assert.Equal(32, System.Buffers.Text.Base64Url.DecodeFromChars(payload.AsSpan("sciphqr:v1:".Length)).Length);
            Assert.Matches("^sha256:[0-9a-f]{64}$", hash);
            Assert.NotEqual(PatientQrToken.Generate().Payload, payload);
        }

        [Fact]
        public void TheHashIsSha256OfTheRawBytes()
        {
            var bytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
            var payload = "sciphqr:v1:" + System.Buffers.Text.Base64Url.EncodeToString(bytes);

            Assert.True(PatientQrToken.TryHash(payload, out var hash));
            Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)), hash);
        }

        [Fact]
        public void AGeneratedPayloadRoundTripsToItsHash()
        {
            var (payload, hash) = PatientQrToken.Generate();

            Assert.True(PatientQrToken.TryHash(payload, out var computed));
            Assert.Equal(hash, computed);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("sciphqr:v1:")]
        [InlineData("sciphqr:v2:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [InlineData("SCIPHQR:v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [InlineData("sciphqr:v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [InlineData("sciphqr:v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [InlineData("sciphqr:v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
        [InlineData("sciphqr:v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")]
        [InlineData("sciphqr:v1:AAAAAAAAAAAAAAAAAAAAA+AAAAAAAAAAAAAAAAAAAAA")]
        [InlineData(" sciphqr:v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [InlineData("sciphqr:v1:AAAAAAAAAAAAAAAAAAAA AAAAAAAAAAAAAAAAAAAAAA")]
        public void AnythingElseIsRejected(string? payload)
        {
            Assert.False(PatientQrToken.TryHash(payload, out var hash));
            Assert.Empty(hash);
        }

        [Fact]
        public void TheUnusedTrailingBitsMustBeZeroSoATokenHasOneSpelling()
        {
            var canonical = "sciphqr:v1:" + new string('A', 43);
            Assert.True(PatientQrToken.TryHash(canonical, out _));

            // The same 32 zero bytes with a trailing bit set decodes to the same value in a lenient decoder.
            Assert.False(PatientQrToken.TryHash("sciphqr:v1:" + new string('A', 42) + "B", out _));
        }

        [Fact]
        public void TheReceiptFingerprintIsNeitherThePayloadNorTheCredentialHash()
        {
            var (payload, hash) = PatientQrToken.Generate();

            var fingerprint = PatientQrToken.ReceiptFingerprint(payload);

            Assert.Matches("^[0-9a-f]{64}$", fingerprint);
            Assert.DoesNotContain(fingerprint, hash);
            Assert.Equal(fingerprint, PatientQrToken.ReceiptFingerprint(payload));
            Assert.NotEqual(fingerprint, PatientQrToken.ReceiptFingerprint(null));
        }

        [Fact]
        public void TheLifetimeIsFifteenMinutes() => Assert.Equal(TimeSpan.FromMinutes(15), PatientQrToken.Lifetime);

        [Fact]
        public void ThePayloadNeverAppearsInTheTextOfAModel()
        {
            var (payload, _) = PatientQrToken.Generate();

            Assert.DoesNotContain(payload, new PatientQrCredential(payload, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow).ToString());
            Assert.DoesNotContain(payload, new ResolvePatientQrRequest(payload).ToString());
            Assert.DoesNotContain(payload, new ConfirmPatientQrRequest(Guid.NewGuid(), payload).ToString());
        }
    }
}
