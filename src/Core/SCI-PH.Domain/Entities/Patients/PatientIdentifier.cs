using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Patients
{
    public sealed class PatientIdentifier
    {
        private const int MinimumPhoneDigits = 7;

        // Identity-bearing types: an active value can belong to only one patient per issuer.
        // Phone, email and other values can be shared, so a match never proves identity.
        public static IReadOnlyCollection<PatientIdentifierType> UniqueTypes { get; } =
        [
            PatientIdentifierType.NationalId,
            PatientIdentifierType.Passport,
            PatientIdentifierType.QrCode,
            PatientIdentifierType.InternalCode
        ];

        private PatientIdentifier() { }

        internal PatientIdentifier(
            Guid patientId,
            PatientIdentifierType type,
            string value,
            PatientIdentifierSource source,
            string? issuer,
            bool isPrimary,
            DateTimeOffset? validUntilUtc)
        {
            var createdAtUtc = DateTimeOffset.UtcNow;
            if (validUntilUtc <= createdAtUtc)
            {
                throw new InvalidPatientIdentifierException("An identifier cannot be created already expired.");
            }

            Id = Guid.NewGuid();
            PatientId = patientId;
            Type = Guard.Defined(type, nameof(type));
            Value = Guard.Text(value, FieldLengths.IdentifierValue, nameof(value));
            NormalizedValue = Normalize(type, value);
            Issuer = NormalizeIssuer(issuer);
            Source = Guard.Defined(source, nameof(source));
            Status = PatientIdentifierStatus.Active;
            IsPrimary = isPrimary;
            CreatedAtUtc = createdAtUtc;
            ValidUntilUtc = validUntilUtc?.ToUniversalTime();
        }

        public Guid Id { get; private set; }
        public Guid PatientId { get; private set; }
        public Patient Patient { get; private set; } = null!;
        public PatientIdentifierType Type { get; private set; }
        public string Value { get; private set; } = string.Empty;
        public string NormalizedValue { get; private set; } = string.Empty;
        // Normalized issuing authority or scope. Empty means the SCI-PH platform scope.
        public string Issuer { get; private set; } = string.Empty;
        public PatientIdentifierSource Source { get; private set; }
        public PatientIdentifierStatus Status { get; private set; }
        public bool IsVerified { get; private set; }
        public bool IsPrimary { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset? VerifiedAtUtc { get; private set; }
        public DateTimeOffset? ValidUntilUtc { get; private set; }
        public DateTimeOffset? RevokedAtUtc { get; private set; }

        public bool IsActive => Status == PatientIdentifierStatus.Active;
        public bool HasUniqueValue => UniqueTypes.Contains(Type);

        public static string Normalize(PatientIdentifierType type, string value)
        {
            var trimmed = value?.Trim() ?? string.Empty;
            var normalized = type switch
            {
                PatientIdentifierType.NationalId or PatientIdentifierType.Passport or PatientIdentifierType.InternalCode =>
                    new string(trimmed.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant(),
                PatientIdentifierType.Phone => NormalizePhone(trimmed),
                PatientIdentifierType.Email => NormalizeEmail(trimmed),
                // QR payloads can be case-sensitive tokens, so only surrounding spaces are removed.
                _ => trimmed
            };

            return normalized.Length == 0
                ? throw new InvalidPatientIdentifierException($"The {type} value is empty after normalization.")
                : normalized;
        }

        public static string NormalizeIssuer(string? issuer) => issuer?.Trim().ToUpperInvariant() ?? string.Empty;

        public bool IsValidAt(DateTimeOffset atUtc) => IsActive && (ValidUntilUtc is null || ValidUntilUtc > atUtc);

        internal bool HasSameValueAs(PatientIdentifier other) =>
            Type == other.Type && Issuer == other.Issuer && NormalizedValue == other.NormalizedValue;

        internal void Promote()
        {
            EnsureActive();
            IsPrimary = true;
        }

        internal void Demote() => IsPrimary = false;

        internal void Verify(DateTimeOffset verifiedAtUtc)
        {
            EnsureActive();
            VerifiedAtUtc = Guard.NotBefore(verifiedAtUtc, CreatedAtUtc, nameof(verifiedAtUtc));
            IsVerified = true;
        }

        internal void Revoke(DateTimeOffset revokedAtUtc)
        {
            EnsureActive();
            RevokedAtUtc = Guard.NotBefore(revokedAtUtc, CreatedAtUtc, nameof(revokedAtUtc));
            Status = PatientIdentifierStatus.Revoked;
            IsPrimary = false;
        }

        private void EnsureActive()
        {
            if (!IsActive)
            {
                throw new InvalidPatientIdentifierException("A revoked identifier cannot be changed.");
            }
        }

        private static string NormalizePhone(string value)
        {
            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length < MinimumPhoneDigits)
            {
                throw new InvalidPatientIdentifierException("A phone number must contain at least seven digits.");
            }

            return value.StartsWith('+') ? "+" + digits : digits;
        }

        private static string NormalizeEmail(string value) =>
            value.Contains('@') ? value.ToLowerInvariant() : throw new InvalidPatientIdentifierException("The email address is not valid.");
    }
}
