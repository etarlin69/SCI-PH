using System.Security.Cryptography;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Errors;
using SkiaSharp;

namespace SCI_PH.Infrastructure.Biometrics.Face
{
    // Input policy of every face capture (ADR-012), independent of the provider that will compare it: at most 2 MB,
    // JPEG or PNG recognized by its magic bytes AND really decoded (the declared Content-Type is not trusted), shorter side
    // at least 480 px and no side over 4096 px once upright. The EXIF orientation is applied, the image is scaled down to
    // at most 1024 px on its longer side and re-encoded as PNG: no EXIF or any other metadata survives. Memory only: the
    // raw bytes are cleared once decoded, nothing is written to disk and nothing about the image is logged.
    public sealed class SkiaFaceCaptureNormalizer : IFaceCaptureNormalizer
    {
        public const int MaxBytes = 2 * 1024 * 1024;
        public const int MinShorterSide = 480;
        public const int MaxSide = 4096;
        public const int NormalizedLongerSide = 1024;

        private const string Field = "capture";

        public async Task<FaceCapture> NormalizeAsync(Stream content, long length, CancellationToken cancellationToken)
        {
            if (length <= 0)
            {
                throw Invalid("capture is empty.");
            }

            if (length > MaxBytes)
            {
                throw Invalid("capture exceeds 2 MB.");
            }

            var raw = new byte[length];
            try
            {
                await content.ReadExactlyAsync(raw, cancellationToken);
                if (!IsJpeg(raw) && !IsPng(raw))
                {
                    throw Invalid("capture must be a JPEG or PNG image.");
                }

                using var data = SKData.CreateCopy(raw);
                using var codec = SKCodec.Create(data) ?? throw Invalid("capture could not be decoded.");
                if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png))
                {
                    throw Invalid("capture must be a JPEG or PNG image.");
                }

                using var decoded = SKBitmap.Decode(codec) ?? throw Invalid("capture could not be decoded.");
                using var upright = Orient(decoded, codec.EncodedOrigin);
                var (width, height) = (upright.Width, upright.Height);
                if (Math.Min(width, height) < MinShorterSide || Math.Max(width, height) > MaxSide)
                {
                    throw Invalid($"capture must be at least {MinShorterSide} px on its shorter side and at most {MaxSide} px.");
                }

                using var scaled = Scale(upright);
                using var image = SKImage.FromBitmap(scaled);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw Invalid("capture could not be decoded.");
                var normalized = png.ToArray();
                return new FaceCapture(normalized, scaled.Width, scaled.Height, Convert.ToHexString(SHA256.HashData(normalized)));
            }
            catch (EndOfStreamException exception)
            {
                throw new RequestValidationException("capture is shorter than declared.", Field, exception);
            }
            finally
            {
                Array.Clear(raw);
            }
        }

        private static RequestValidationException Invalid(string message) => new(message, Field);

        private static bool IsJpeg(byte[] bytes) => bytes is [0xFF, 0xD8, 0xFF, ..];

        private static bool IsPng(byte[] bytes) => bytes is [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..];

        // Applies the EXIF orientation, so the pixels are upright without any metadata.
        private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
        {
            var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var target = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height, source.ColorType, source.AlphaType);
            using var canvas = new SKCanvas(target);
            var (w, h) = (source.Width, source.Height);
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1); canvas.Translate(-w, 0); break;
                case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180); canvas.Translate(-w, -h); break;
                case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1); canvas.Translate(0, -h); break;
                case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(h, w); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, w); canvas.RotateDegrees(270); break;
            }

            canvas.DrawBitmap(source, 0, 0);
            return target;
        }

        private static SKBitmap Scale(SKBitmap upright)
        {
            var longer = Math.Max(upright.Width, upright.Height);
            if (longer <= NormalizedLongerSide)
            {
                return upright.Copy();
            }

            var factor = (double)NormalizedLongerSide / longer;
            var info = new SKImageInfo(
                Math.Max(1, (int)Math.Round(upright.Width * factor)), Math.Max(1, (int)Math.Round(upright.Height * factor)),
                upright.ColorType, upright.AlphaType);
            return upright.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                ?? throw new RequestValidationException("capture could not be decoded.", Field);
        }
    }
}
