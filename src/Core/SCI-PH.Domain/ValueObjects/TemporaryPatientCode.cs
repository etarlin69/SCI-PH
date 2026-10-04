using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.ValueObjects
{
    // Temporary reference for an unidentified (NN) patient. Kept after identification as history.
    public sealed record TemporaryPatientCode
    {
        public const string Prefix = "NN-";
        public const int MaxLength = 40;
        private const int SuffixLength = 32;

        private TemporaryPatientCode(string value) => Value = value;

        public string Value { get; }

        public static TemporaryPatientCode New() => new(Prefix + Guid.NewGuid().ToString("N").ToUpperInvariant());

        public static TemporaryPatientCode Parse(string value)
        {
            var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
            var suffix = normalized.StartsWith(Prefix, StringComparison.Ordinal) ? normalized[Prefix.Length..] : string.Empty;
            if (suffix.Length != SuffixLength || !suffix.All(Uri.IsHexDigit))
            {
                throw new DomainException($"'{value}' is not a valid temporary patient code.");
            }

            return new TemporaryPatientCode(normalized);
        }

        public override string ToString() => Value;
    }
}
