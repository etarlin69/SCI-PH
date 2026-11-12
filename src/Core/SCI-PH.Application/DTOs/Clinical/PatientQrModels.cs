using System.Text.Json.Serialization;

namespace SCI_PH.Application.DTOs.Clinical
{
    // The QR content, once, for the app to draw. Never a patient id, a hash or an image. ToString never prints the payload.
    public sealed record PatientQrCredential(string Payload, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc)
    {
        public override string ToString() => nameof(PatientQrCredential);
    }

    // The scanned QR content always travels in the body, never in a route or query string.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ResolvePatientQrRequest([property: JsonRequired] string Payload)
    {
        public override string ToString() => nameof(ResolvePatientQrRequest);
    }

    // The staff member's confirmation of the candidate. No patient id: the server resolves the QR again.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ConfirmPatientQrRequest(
        [property: JsonRequired] Guid ClientRequestId,
        [property: JsonRequired] string Payload)
    {
        public override string ToString() => nameof(ConfirmPatientQrRequest);
    }

    // Enough for a human confirmation and nothing else: no patient or account id, no clinical data, no biometric reference.
    public sealed record PatientQrCandidate(string DisplayName, DateOnly? DateOfBirth, PatientIdentifierSummary? Document);
}
