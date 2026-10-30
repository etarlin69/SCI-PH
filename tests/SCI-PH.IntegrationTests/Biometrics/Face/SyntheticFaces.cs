using OpenCvSharp;
using SCI_PH.Infrastructure.Biometrics.Face.OpenCv;

namespace SCI_PH.IntegrationTests.Biometrics.Face
{
    // Real-model test inputs (opt-in, SCIPH_FACE_MODELS=1): the pinned models from scripts/biometrics/fetch-face-models and
    // the Face Synthetics sample from scripts/biometrics/fetch-face-fixtures (synthetic, rendered faces; Microsoft Research
    // Use of Data Agreement: local academic testing only, never committed, published or put in an image or a CI artifact).
    internal static class SyntheticFaces
    {
        public const string SkipReason = "Set SCIPH_FACE_MODELS=1 (after fetching the models and the fixtures) to run the real face models.";

        public static bool Enabled => Environment.GetEnvironmentVariable("SCIPH_FACE_MODELS") == "1";

        public static string Models => Environment.GetEnvironmentVariable("SCIPH_FACE_MODELS_DIR") ?? Path.Combine(Root, ".models", "face");

        public static string YuNet => Path.Combine(Models, FaceModels.YuNetFileName);

        public static string SFace => Path.Combine(Models, FaceModels.SFaceFileName);

        private static string Fixtures =>
            Environment.GetEnvironmentVariable("SCIPH_FACE_FIXTURES_DIR") ?? Path.Combine(Root, ".fixtures", "face-synthetics");

        private static string Root
        {
            get
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SCI-PH.sln")))
                {
                    directory = directory.Parent;
                }

                return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
            }
        }

        // One synthetic identity of the sample (000000.png, 000001.png, ...), 512 x 512.
        public static Mat Face(int index) => Cv2.ImRead(Path.Combine(Fixtures, $"{index:000000}.png"), ImreadModes.Color);

        // The same identity in another capture: larger and brighter.
        public static Mat Transformed(Mat face)
        {
            using var scaled = new Mat();
            Cv2.Resize(face, scaled, new Size(), 1.2, 1.2);
            var result = new Mat();
            scaled.ConvertTo(result, -1, 1.1, 12);
            return result;
        }

        // Two identities side by side.
        public static Mat Pair(Mat left, Mat right)
        {
            var result = new Mat();
            Cv2.HConcat([left, right], result);
            return result;
        }

        public static Mat Black() => new(new Size(640, 480), MatType.CV_8UC3, Scalar.Black);

        public static byte[] Png(Mat image)
        {
            Cv2.ImEncode(".png", image, out var png);
            return png;
        }
    }
}
