using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace SCI_PH.Application.Features.Clinical
{
    // The opaque content of a patient QR (ADR-015): "sciphqr:v1:" + 32 random bytes in base64url without padding. It holds
    // no patient, account, personal or clinical data. The server keeps only "sha256:" + the lowercase hex hash of the raw
    // bytes, so a stored value cannot be turned back into a usable QR. 256 random bits need no salt or slow hash.
    public static class PatientQrToken
    {
        public const string Prefix = "sciphqr:v1:";
        public const string HashPrefix = "sha256:";
        public const int TokenBytes = 32;

        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

        // Base64url of 32 bytes without padding.
        private const int EncodedLength = 43;

        private static readonly SearchValues<char> _alphabet =
            SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

        // The payload goes to the patient once and is never stored; the hash is what persists.
        public static (string Payload, string Hash) Generate()
        {
            var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
            return (Prefix + Base64Url.EncodeToString(bytes), HashOf(bytes));
        }

        // Only the one canonical spelling of the current version is valid: no spaces, padding, other versions or legacy
        // formats, and the unused trailing bits of the encoding must be zero, so one token has exactly one payload.
        public static bool TryHash(string? payload, out string hash)
        {
            hash = string.Empty;
            if (payload is null
                || payload.Length != Prefix.Length + EncodedLength
                || !payload.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            // The decoder tolerates spaces and padding, so the alphabet is checked first, and it throws (instead of returning
            // false) on nonzero trailing bits: anything it cannot read is just another invalid QR, never a server error.
            var encoded = payload.AsSpan(Prefix.Length);
            if (encoded.ContainsAnyExcept(_alphabet))
            {
                return false;
            }

            var bytes = new byte[TokenBytes];
            try
            {
                if (!Base64Url.TryDecodeFromChars(encoded, bytes, out var written) || written != TokenBytes)
                {
                    return false;
                }
            }
            catch (FormatException)
            {
                return false;
            }

            if (!encoded.SequenceEqual(Base64Url.EncodeToString(bytes)))
            {
                return false;
            }

            hash = HashOf(bytes);
            return true;
        }

        // For the idempotency receipt of a confirmation, whatever the payload looks like: never the payload itself, and
        // not the stored credential hash either.
        public static string ReceiptFingerprint(string? payload) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload ?? string.Empty)));

        private static string HashOf(byte[] bytes) => HashPrefix + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
