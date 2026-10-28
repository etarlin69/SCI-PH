using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Errors;

namespace SCI_PH.Infrastructure.Biometrics.Face
{
    // No face provider configured (Production until a real provider is set up): every operation is a 503, never a result.
    public sealed class DisabledFaceVerificationService : IFaceVerificationService
    {
        public string Provider => "SCIPH-FaceDisabled";

        public string AlgorithmVersion => "none";

        public Task<FaceEnrollmentResult> EnrollAsync(FaceCapture capture, CancellationToken cancellationToken) => throw Unavailable();

        public Task<FaceVerificationOutcome> VerifyAsync(string subjectReference, FaceCapture capture, CancellationToken cancellationToken) =>
            throw Unavailable();

        public Task<FaceDeletionOutcome> DeleteAsync(string subjectReference, CancellationToken cancellationToken) => throw Unavailable();

        private static ExternalServiceUnavailableException Unavailable() =>
            new("The face verification service is not available. Try again later.");
    }
}
