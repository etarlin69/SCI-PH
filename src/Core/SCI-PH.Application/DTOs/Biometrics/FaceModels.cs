using System.Text.Json.Serialization;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Serialization;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.DTOs.Biometrics
{
    // ---- Face verification: Web BFF (hospital staff, ADR-012) ----

    // A face reference of the patient: never its SubjectReference, template, score or threshold. Provider: the provider's
    // stable name (the Web shows a friendly label for it).
    public sealed record PatientFaceSummary(
        Guid Id,
        [property: JsonConverter(typeof(TextEnumConverter))] BiometricReferenceStatus Status,
        string Provider,
        DateTimeOffset RegisteredAtUtc,
        DateTimeOffset? RevokedAtUtc);

    // A capture uploaded as multipart/form-data: the image (memory only) and the client's request id.
    public sealed record FaceCaptureUpload(Guid ClientRequestId, Stream Content, long Length);

    // face: the created reference, only when Enrolled. NoFace, MultipleFaces and LowQuality create nothing.
    public sealed record FaceEnrollmentResponse(
        [property: JsonConverter(typeof(TextEnumConverter))] FaceEnrollmentOutcome Outcome,
        PatientFaceSummary? Face);

    public sealed record EnrollFaceResult(FaceEnrollmentResponse Response, bool Created);

    // Only the outcome: never a score, a threshold or the reference compared with.
    public sealed record FaceVerificationResponse(
        Guid VerificationId,
        [property: JsonConverter(typeof(TextEnumConverter))] FaceVerificationOutcome Outcome);

    // Why the staff member revoked it; kept only in the audit entry.
    public enum FaceRevocationReason
    {
        PatientRequest = 1,
        EnrollmentError = 2,
        ReferenceOutdated = 3,
        Other = 4
    }

    // reason: optional. Patient, reference, provider and dates come from the route, the session and the server.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RevokeFaceRequest(
        [property: JsonConverter(typeof(TextEnumConverter))] FaceRevocationReason? Reason);
}
