using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using SCI_PH.Application.Abstractions.Security;

namespace SCI_PH.Infrastructure.Security
{
    // Random secrets from RandomNumberGenerator, stored as lowercase SHA-256 hex and compared in constant time.
    public sealed class SecureTokenGenerator : ISecureTokenGenerator
    {
        private const int HashBytes = 32;

        public string Generate(int byteCount) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(byteCount));

        public string Hash(string value) => Convert.ToHexStringLower(ComputeHash(value));

        public bool Matches(string value, string expectedHash)
        {
            var actual = ComputeHash(value);
            var expected = expectedHash.Length == HashBytes * 2 && expectedHash.All(Uri.IsHexDigit)
                ? Convert.FromHexString(expectedHash)
                : new byte[HashBytes];
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private static byte[] ComputeHash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }
}
