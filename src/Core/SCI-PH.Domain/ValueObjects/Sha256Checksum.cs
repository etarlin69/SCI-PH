using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.ValueObjects
{
    // Format only: the storage service must compute the hash from the real file content.
    public sealed record Sha256Checksum
    {
        public const int Length = 64;

        private Sha256Checksum(string value) => Value = value;

        public string Value { get; }

        public static Sha256Checksum Parse(string value)
        {
            var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalized.Length != Length || !normalized.All(Uri.IsHexDigit))
            {
                throw new DomainException("A SHA-256 checksum must contain 64 hexadecimal characters.");
            }

            return new Sha256Checksum(normalized);
        }

        public override string ToString() => Value;
    }
}
