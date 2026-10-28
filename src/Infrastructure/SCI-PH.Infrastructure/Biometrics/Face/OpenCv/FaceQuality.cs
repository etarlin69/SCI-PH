using SCI_PH.Application.Abstractions.Biometrics;

namespace SCI_PH.Infrastructure.Biometrics.Face.OpenCv
{
    // One YuNet detection in capture pixels.
    internal readonly record struct DetectedFace(float X, float Y, float Width, float Height, float Score);

    // Which detected face, if any, is good enough to embed (ADR-012, PR 2). Pure, so it is tested without OpenCV.
    //   no face at or over DetectorScore            -> NoFace
    //   two or more faces at or over GoodScore      -> MultipleFaces
    //   best face under GoodScore                   -> LowQuality
    //   best face's shorter side under MinFaceSide  -> LowQuality
    //   more than 10% of the best face's box outside the image -> LowQuality
    // No blur or pose scoring in this PR.
    internal static class FaceQuality
    {
        public const double MaxClippedFraction = 0.10;

        public static FaceVerificationOutcome? Assess(
            IReadOnlyList<DetectedFace> faces, int imageWidth, int imageHeight, OpenCvFaceOptions options, out int bestIndex)
        {
            bestIndex = -1;
            var good = 0;
            for (var i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                if (face.Score < options.DetectorScore)
                {
                    continue;
                }

                if (face.Score >= options.GoodScore)
                {
                    good++;
                }

                if (bestIndex < 0 || face.Score > faces[bestIndex].Score)
                {
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                return FaceVerificationOutcome.NoFace;
            }

            if (good > 1)
            {
                return FaceVerificationOutcome.MultipleFaces;
            }

            var best = faces[bestIndex];
            if (best.Score < options.GoodScore
                || Math.Min(best.Width, best.Height) < options.MinFaceSide
                || ClippedFraction(best, imageWidth, imageHeight) > MaxClippedFraction)
            {
                return FaceVerificationOutcome.LowQuality;
            }

            return null;
        }

        public static double ClippedFraction(DetectedFace face, int imageWidth, int imageHeight)
        {
            double area = face.Width * face.Height;
            if (area <= 0)
            {
                return 1;
            }

            var insideWidth = Math.Max(0, Math.Min(face.X + face.Width, imageWidth) - Math.Max(face.X, 0));
            var insideHeight = Math.Max(0, Math.Min(face.Y + face.Height, imageHeight) - Math.Max(face.Y, 0));
            return 1 - insideWidth * insideHeight / area;
        }

        // Cosine similarity of two L2-normalized embeddings.
        public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            if (a.Length != b.Length)
            {
                throw new FaceTemplateUnavailableException("FaceTemplateDimensionsInvalid");
            }

            double dot = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
            }

            return dot;
        }

        public static void Normalize(Span<float> vector)
        {
            double sum = 0;
            foreach (var value in vector)
            {
                sum += value * value;
            }

            var norm = Math.Sqrt(sum);
            if (norm <= 0 || double.IsNaN(norm) || double.IsInfinity(norm))
            {
                throw new InvalidOperationException("The face embedding is not usable.");
            }

            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)(vector[i] / norm);
            }
        }
    }
}
