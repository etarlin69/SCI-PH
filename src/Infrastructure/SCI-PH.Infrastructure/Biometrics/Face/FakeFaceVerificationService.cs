using System.Collections.Concurrent;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Errors;
using SkiaSharp;

namespace SCI_PH.Infrastructure.Biometrics.Face
{
    // Deterministic face provider for CI, integration tests, the end-to-end of PR 1 and development without models or keys
    // (ADR-012). It does NOT recognize faces: it reads the mean colour of a synthetic, programmatically generated capture.
    //   black         -> NoFace           red  -> MultipleFaces    mid grey -> LowQuality
    //   pure blue     -> provider failure (ExternalServiceUnavailableException, 503)
    //   any other     -> a "face" whose signature is that colour, quantized: the same colour matches, another does not.
    // Its "templates" (that signature) live only in this process's memory, keyed by an opaque random subject reference;
    // nothing is written to the database, the disk or the logs. Never allowed in Production (AddFaceVerification).
    public sealed class FakeFaceVerificationService : IFaceVerificationService
    {
        public const string ProviderName = "SCIPH-FaceFake";
        public const string Version = "fake-v1";

        private readonly ConcurrentDictionary<string, string> _subjects = new(StringComparer.Ordinal);

        public string Provider => ProviderName;

        public string AlgorithmVersion => Version;

        // For tests: whether a subject is still held (enrolled and not deleted or compensated), and how many are.
        public bool Holds(string subjectReference) => _subjects.ContainsKey(subjectReference);

        public int Count => _subjects.Count;

        public Task<FaceEnrollmentResult> EnrollAsync(FaceCapture capture, CancellationToken cancellationToken)
        {
            if (Classify(capture) is { } rejected)
            {
                return Task.FromResult(FaceEnrollmentResult.Rejected(rejected switch
                {
                    FaceVerificationOutcome.NoFace => FaceEnrollmentOutcome.NoFace,
                    FaceVerificationOutcome.MultipleFaces => FaceEnrollmentOutcome.MultipleFaces,
                    _ => FaceEnrollmentOutcome.LowQuality
                }));
            }

            var subjectReference = $"face:{Guid.NewGuid():D}";
            _subjects[subjectReference] = Signature(capture);
            return Task.FromResult(new FaceEnrollmentResult(FaceEnrollmentOutcome.Enrolled, subjectReference));
        }

        public Task<FaceVerificationOutcome> VerifyAsync(string subjectReference, FaceCapture capture, CancellationToken cancellationToken)
        {
            if (Classify(capture) is { } rejected)
            {
                return Task.FromResult(rejected);
            }

            // A real provider that lost a template fails; it does not answer "no match".
            if (!_subjects.TryGetValue(subjectReference, out var enrolled))
            {
                throw Unavailable();
            }

            return Task.FromResult(enrolled == Signature(capture) ? FaceVerificationOutcome.Match : FaceVerificationOutcome.NoMatch);
        }

        public Task<FaceDeletionOutcome> DeleteAsync(string subjectReference, CancellationToken cancellationToken) =>
            Task.FromResult(_subjects.TryRemove(subjectReference, out _) ? FaceDeletionOutcome.Deleted : FaceDeletionOutcome.NotFound);

        private static FaceVerificationOutcome? Classify(FaceCapture capture)
        {
            var (r, g, b) = MeanColour(capture);
            if (b > 200 && r < 60 && g < 60)
            {
                throw Unavailable();
            }

            if (Math.Max(r, Math.Max(g, b)) < 24)
            {
                return FaceVerificationOutcome.NoFace;
            }

            if (r > 200 && g < 60 && b < 60)
            {
                return FaceVerificationOutcome.MultipleFaces;
            }

            return Math.Abs(r - 128) < 16 && Math.Abs(g - 128) < 16 && Math.Abs(b - 128) < 16
                ? FaceVerificationOutcome.LowQuality
                : null;
        }

        private static string Signature(FaceCapture capture)
        {
            var (r, g, b) = MeanColour(capture);
            return $"{r / 16}-{g / 16}-{b / 16}";
        }

        private static (int R, int G, int B) MeanColour(FaceCapture capture)
        {
            using var bitmap = SKBitmap.Decode(capture.Image)
                ?? throw new InvalidOperationException("A normalized face capture could not be decoded.");
            long r = 0, g = 0, b = 0;
            var pixels = bitmap.Pixels;
            foreach (var pixel in pixels)
            {
                (r, g, b) = (r + pixel.Red, g + pixel.Green, b + pixel.Blue);
            }

            var count = Math.Max(1, pixels.Length);
            return ((int)(r / count), (int)(g / count), (int)(b / count));
        }

        private static ExternalServiceUnavailableException Unavailable() =>
            new("The face verification service is not available. Try again later.");
    }
}
