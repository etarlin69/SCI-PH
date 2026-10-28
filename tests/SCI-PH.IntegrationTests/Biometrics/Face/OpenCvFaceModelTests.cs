using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Infrastructure.Biometrics.Face;
using SCI_PH.Infrastructure.Biometrics.Face.OpenCv;

namespace SCI_PH.IntegrationTests.Biometrics.Face
{
    // The real pipeline (YuNet + quality gate + SFace + encrypted template) on synthetic faces. OPT-IN (SyntheticFaces):
    // normal CI skips it; the native load is covered there by OpenCvFaceProviderTests.TheNativeOpenCvRuntimeLoads.
    // Thresholds are the upstream-derived defaults, NOT calibrated for SCI-PH webcams: these tests prove the pipeline, not
    // an accuracy figure.
    public class OpenCvFaceModelTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        private static FaceEnginePool Pool(int capacity = 1) =>
            new(() => new FaceEngine(SyntheticFaces.YuNet, SyntheticFaces.SFace, new OpenCvFaceOptions()), capacity);

        // The capture as the Web BFF would hand it to the provider: through the real normalizer.
        private static async Task<FaceCapture> CaptureAsync(Mat image)
        {
            var png = SyntheticFaces.Png(image);
            using var stream = new MemoryStream(png);
            return await new SkiaFaceCaptureNormalizer().NormalizeAsync(stream, png.Length, None);
        }

        private static LocalOpenCvFaceVerificationService Service(FaceEnginePool pool) =>
            new(pool, new OpenCvFaceProviderTests.InMemoryTemplateStore(),
                new FaceTemplateCipher("k1", new Dictionary<string, byte[]> { ["k1"] = RandomNumberGenerator.GetBytes(32) }),
                new OpenCvFaceOptions(), NullLogger<LocalOpenCvFaceVerificationService>.Instance);

        [SkippableFact]
        public async Task TheSameSyntheticIdentityMatchesAndAnotherDoesNot()
        {
            Skip.IfNot(SyntheticFaces.Enabled, SyntheticFaces.SkipReason);
            using var pool = Pool();
            var faces = Service(pool);
            using var a = SyntheticFaces.Face(0);
            using var aAgain = SyntheticFaces.Transformed(a);
            using var b = SyntheticFaces.Face(1);

            var enrolled = await faces.EnrollAsync(await CaptureAsync(a), None);

            Assert.Equal(FaceEnrollmentOutcome.Enrolled, enrolled.Outcome);
            Assert.Equal(FaceVerificationOutcome.Match, await faces.VerifyAsync(enrolled.SubjectReference!, await CaptureAsync(aAgain), None));
            Assert.Equal(FaceVerificationOutcome.NoMatch, await faces.VerifyAsync(enrolled.SubjectReference!, await CaptureAsync(b), None));
        }

        [SkippableFact]
        public async Task CapturesWithoutOneGoodFaceAreRejected()
        {
            Skip.IfNot(SyntheticFaces.Enabled, SyntheticFaces.SkipReason);
            using var pool = Pool();
            using var a = SyntheticFaces.Face(0);
            using var b = SyntheticFaces.Face(1);

            using var black = SyntheticFaces.Black();
            Assert.Equal(FaceVerificationOutcome.NoFace, (await pool.AnalyzeAsync((await CaptureAsync(black)).Image, None)).Rejected);

            using var two = SyntheticFaces.Pair(a, b);
            Assert.Equal(FaceVerificationOutcome.MultipleFaces, (await pool.AnalyzeAsync((await CaptureAsync(two)).Image, None)).Rejected);

            // A face far too small for the gate (shorter side under 80 px) on a large canvas.
            using var small = new Mat();
            Cv2.Resize(a, small, new Size(150, 150));
            using var canvas = new Mat(new Size(640, 480), MatType.CV_8UC3, Scalar.All(40));
            small.CopyTo(new Mat(canvas, new Rect(245, 165, 150, 150)));
            Assert.Equal(FaceVerificationOutcome.LowQuality, (await pool.AnalyzeAsync((await CaptureAsync(canvas)).Image, None)).Rejected);
        }

        // The pool bounds the concurrent pairs and gives every caller a consistent result.
        [SkippableFact]
        public async Task TheEnginePoolIsBoundedAndThreadSafe()
        {
            Skip.IfNot(SyntheticFaces.Enabled, SyntheticFaces.SkipReason);
            using var pool = Pool(capacity: 2);
            using var a = SyntheticFaces.Face(0);
            var capture = await CaptureAsync(a);
            var reference = (await pool.AnalyzeAsync(capture.Image, None)).Embedding!;

            var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => pool.AnalyzeAsync(capture.Image, None))));

            Assert.All(results, r => Assert.True(FaceQuality.Cosine(reference, r.Embedding!) > 0.999));
            Assert.InRange(pool.Created, 1, 2);
        }
    }
}
