using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace SCI_PH.IntegrationTests.Security.Support
{
    // RFC 6238 code generator (what an authenticator app does), used only by tests.
    internal static class Totp
    {
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        // stepOffset selects a neighbouring 30-second step; Identity accepts codes up to two steps away.
        public static string Code(string base32Key, int stepOffset = 0)
        {
            Span<byte> counter = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + stepOffset);
            var hash = HMACSHA1.HashData(DecodeBase32(base32Key), counter);
            var offset = hash[^1] & 0x0F;
            var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
            return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        }

        private static byte[] DecodeBase32(string value)
        {
            var output = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (var character in value.ToUpperInvariant().Where(c => c is not ('=' or ' ')))
            {
                buffer = (buffer << 5) | Base32Alphabet.IndexOf(character);
                bits += 5;
                if (bits >= 8)
                {
                    output.Add((byte)(buffer >> (bits - 8)));
                    bits -= 8;
                }
            }

            return output.ToArray();
        }
    }
}
