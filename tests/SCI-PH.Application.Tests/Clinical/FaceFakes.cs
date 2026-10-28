using System.Security.Cryptography;
using System.Text;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Errors;

namespace SCI_PH.Application.Tests.Clinical
{
    // A capture for the Application tests: its text is its "face" ("face:A", "noface", "multiple", "lowquality", "down").
    internal static class TestCaptures
    {
        public static Stream Of(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));
    }

    // Normalization is Infrastructure's (SkiaFaceCaptureNormalizer); here the bytes pass through unchanged.
    internal sealed class PassThroughCaptureNormalizer : IFaceCaptureNormalizer
    {
        public async Task<FaceCapture> NormalizeAsync(Stream content, long length, CancellationToken cancellationToken)
        {
            if (length <= 0)
            {
                throw new RequestValidationException("capture is empty.", "capture");
            }

            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            return new FaceCapture(bytes, 640, 480, Convert.ToHexString(SHA256.HashData(bytes)));
        }
    }

    // Records every call; the capture's text decides the outcome. Throws ExternalServiceUnavailableException for "down".
    internal sealed class TestFaceProvider : IFaceVerificationService
    {
        public Dictionary<string, string> Subjects { get; } = [];
        public List<string> Calls { get; } = [];
        public bool FailDeletes { get; set; }

        public string Provider => "TEST-Face";

        public string AlgorithmVersion => "test-v1";

        public Task<FaceEnrollmentResult> EnrollAsync(FaceCapture capture, CancellationToken cancellationToken)
        {
            Calls.Add("enroll");
            var face = Text(capture);
            if (Rejected(face) is { } outcome)
            {
                return Task.FromResult(FaceEnrollmentResult.Rejected(outcome switch
                {
                    FaceVerificationOutcome.NoFace => FaceEnrollmentOutcome.NoFace,
                    FaceVerificationOutcome.MultipleFaces => FaceEnrollmentOutcome.MultipleFaces,
                    _ => FaceEnrollmentOutcome.LowQuality
                }));
            }

            var subject = $"face:{Guid.NewGuid():D}";
            Subjects[subject] = face;
            return Task.FromResult(new FaceEnrollmentResult(FaceEnrollmentOutcome.Enrolled, subject));
        }

        public Task<FaceVerificationOutcome> VerifyAsync(string subjectReference, FaceCapture capture, CancellationToken cancellationToken)
        {
            Calls.Add($"verify:{subjectReference}");
            var face = Text(capture);
            return Task.FromResult(Rejected(face) ?? (Subjects[subjectReference] == face ? FaceVerificationOutcome.Match : FaceVerificationOutcome.NoMatch));
        }

        public Task<FaceDeletionOutcome> DeleteAsync(string subjectReference, CancellationToken cancellationToken)
        {
            Calls.Add($"delete:{subjectReference}");
            if (FailDeletes)
            {
                throw new ExternalServiceUnavailableException("down");
            }

            return Task.FromResult(Subjects.Remove(subjectReference) ? FaceDeletionOutcome.Deleted : FaceDeletionOutcome.NotFound);
        }

        private static string Text(FaceCapture capture) => Encoding.UTF8.GetString(capture.Image);

        private static FaceVerificationOutcome? Rejected(string face) => face switch
        {
            "down" => throw new ExternalServiceUnavailableException("The face verification service is not available. Try again later."),
            "noface" => FaceVerificationOutcome.NoFace,
            "multiple" => FaceVerificationOutcome.MultipleFaces,
            "lowquality" => FaceVerificationOutcome.LowQuality,
            _ => null
        };
    }
}
