namespace SCI_PH.Application.Abstractions.Biometrics
{
    // 1:1 face verification provider (ADR-012). Application only knows this contract: never a model, an SDK, an embedding,
    // a similarity score, a threshold or where the provider keeps its templates. The provider owns its template store; the
    // clinical schema only keeps the opaque SubjectReference it returns (PatientBiometricReference, Modality Face).
    // Captures arrive already validated and normalized (IFaceCaptureNormalizer). Match and NoMatch are decided by the
    // provider with server-side configuration; the client never sends a threshold. Any provider failure (unavailable,
    // timeout, internal error) is a FaceProviderUnavailableException (503), never one of these outcomes.
    public interface IFaceVerificationService
    {
        // Stable provider name, stored as PatientBiometricReference.Provider (e.g. "SCIPH-FaceFake").
        string Provider { get; }

        // Version of the provider's algorithm, stored as PatientBiometricReference.AlgorithmVersion.
        string AlgorithmVersion { get; }

        // Creates a template from the capture. Enrolled carries the provider's opaque subject reference ("face:<guid>"),
        // which never contains patient data; the other outcomes create nothing.
        Task<FaceEnrollmentResult> EnrollAsync(FaceCapture capture, CancellationToken cancellationToken);

        // Compares the capture with ONE enrolled subject (1:1, never a search among subjects).
        Task<FaceVerificationOutcome> VerifyAsync(string subjectReference, FaceCapture capture, CancellationToken cancellationToken);

        // Removes the subject's template. Not part of security: a revoked reference is never verified again anyway.
        Task<FaceDeletionOutcome> DeleteAsync(string subjectReference, CancellationToken cancellationToken);
    }

    public sealed record FaceEnrollmentResult(FaceEnrollmentOutcome Outcome, string? SubjectReference)
    {
        public static FaceEnrollmentResult Rejected(FaceEnrollmentOutcome outcome) => new(outcome, null);
    }

    public enum FaceEnrollmentOutcome
    {
        Enrolled = 1,
        NoFace = 2,
        MultipleFaces = 3,
        LowQuality = 4
    }

    public enum FaceVerificationOutcome
    {
        Match = 1,
        NoMatch = 2,
        NoFace = 3,
        MultipleFaces = 4,
        LowQuality = 5
    }

    public enum FaceDeletionOutcome
    {
        Deleted = 1,
        NotFound = 2
    }

    // Normalizes a raw capture before any provider sees it: real decoding (not the declared Content-Type), size and
    // dimension limits, orientation applied, metadata (EXIF) removed, re-encoded. Invalid input is a
    // RequestValidationException (400). Memory only: nothing is written to disk, logs or storage.
    public interface IFaceCaptureNormalizer
    {
        Task<FaceCapture> NormalizeAsync(Stream content, long length, CancellationToken cancellationToken);
    }

    // A normalized capture: PNG without metadata, upright. Never logged (ToString does not print the bytes) and never
    // persisted. ContentHash: SHA-256 of the normalized image, only to recognize a retried request.
    public sealed class FaceCapture
    {
        public FaceCapture(byte[] image, int width, int height, string contentHash)
        {
            Image = image;
            Width = width;
            Height = height;
            ContentHash = contentHash;
        }

        public byte[] Image { get; }
        public int Width { get; }
        public int Height { get; }
        public string ContentHash { get; }

        public override string ToString() => $"FaceCapture({Width}x{Height})";
    }
}
