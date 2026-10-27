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

        // The slot range of the sensor is not fixed here (not verified against the R307 documentation): any non-negative
        // slot is a valid reference format.
        public static string SubjectReference(Guid deviceCredentialId, int slot)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(slot);
            return string.Create(CultureInfo.InvariantCulture, $"r307:device:{deviceCredentialId:D}:slot:{slot}");
        }
    }
}
