using System.Globalization;

namespace SCI_PH.Application.Common.Biometrics
{
    // Fingerprint references of the prototype (ADR-009). The R307 keeps the template in one of its slots and matches on the
    // sensor; SCI-PH only stores which (reader, slot) belongs to which patient, as a PatientBiometricReference with
    // Provider "R307" and this SubjectReference. It is built from the reader's DeviceCredential id (stable; the device code
    // is a human label that could change) and is never sent to the app or the reader, nor written to logs or audit.
    // A reference belongs to ONE reader: a patient enrolled on reader A is not recognized by reader B.
    public static class FingerprintReferences
    {
        public const string Provider = "R307";

        // A session waits this long for the reader and, once matched, for the paramedic's confirmation.
        public static readonly TimeSpan SessionTtl = TimeSpan.FromSeconds(90);

        // Prototype enrollment session lifetime (ADR-010); the R307 timing (captures per enrollment) is pending verification.
        public static readonly TimeSpan EnrollmentTtl = TimeSpan.FromSeconds(120);

        // The slot range of the sensor is not fixed here (not verified against the R307 documentation): any non-negative
        // slot is a valid reference format.
        public static string SubjectReference(Guid deviceCredentialId, int slot)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(slot);
            return string.Create(CultureInfo.InvariantCulture, $"{ReaderPrefix(deviceCredentialId)}{slot}");
        }

        // Every subject reference of one reader starts with this: a patient's references on a reader are found by it.
        public static string ReaderPrefix(Guid deviceCredentialId) =>
            string.Create(CultureInfo.InvariantCulture, $"r307:device:{deviceCredentialId:D}:slot:");

        // The reader of a subject reference, or null when it does not follow this format.
        public static Guid? ReaderOf(string subjectReference) =>
            TryParse(subjectReference, out var deviceCredentialId, out _) ? deviceCredentialId : null;

        // The only parser of the format: exactly "r307:device:<GUID in D format>:slot:<non-negative integer>", and only when
        // building it again gives the same text (no sign, no leading zeros, no spaces). Anything else is not a reference of
        // this prototype (a legacy or foreign one): its reader and slot are unknown and nothing physical is asked of a reader.
        public static bool TryParse(string? subjectReference, out Guid deviceCredentialId, out int slot)
        {
            const string head = "r307:device:";
            const string slotSeparator = ":slot:";
            deviceCredentialId = Guid.Empty;
            slot = -1;
            if (subjectReference is null || !subjectReference.StartsWith(head, StringComparison.Ordinal))
            {
                return false;
            }

            var rest = subjectReference.AsSpan(head.Length);
            var separator = rest.IndexOf(slotSeparator, StringComparison.Ordinal);
            if (separator <= 0
                || !Guid.TryParseExact(rest[..separator], "D", out var id)
                || !int.TryParse(rest[(separator + slotSeparator.Length)..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                || !string.Equals(SubjectReference(id, parsed), subjectReference, StringComparison.Ordinal))
            {
                return false;
            }

            (deviceCredentialId, slot) = (id, parsed);
            return true;
        }
    }
}
