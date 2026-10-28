using System.Text;
using SkiaSharp;

namespace SCI_PH.IntegrationTests.Clinical.Support
{
    // Synthetic captures for the deterministic face provider (FakeFaceVerificationService): solid colours generated here,
    // never a photograph of a person. Black is "no face", red "several faces", mid grey "low quality", pure blue a
    // provider failure; any other colour is one "face", and the same colour matches it.
    internal static class FaceFixtures
    {
        public static readonly SKColor FaceA = new(200, 150, 100);
        public static readonly SKColor FaceB = new(90, 160, 90);
        public static readonly SKColor NoFace = new(0, 0, 0);
        public static readonly SKColor MultipleFaces = new(230, 20, 20);
        public static readonly SKColor LowQuality = new(128, 128, 128);
        public static readonly SKColor ProviderFailure = new(0, 0, 255);

        public static byte[] Png(SKColor colour, int width = 640, int height = 480) => Encode(colour, width, height, SKEncodedImageFormat.Png);

        public static byte[] Jpeg(SKColor colour, int width = 640, int height = 480) => Encode(colour, width, height, SKEncodedImageFormat.Jpeg);

        // A JPEG with an EXIF APP1 segment: an orientation and, optionally, a text that must never survive normalization.
        public static byte[] JpegWithExif(SKColor colour, int width, int height, ushort orientation, string? marker = null)
        {
            var jpeg = Jpeg(colour, width, height);
            var tiff = new List<byte>
            {
                (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // little endian, first IFD at 8
                0x01, 0x00,                                               // one entry
                0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,           // 0x0112 Orientation, SHORT, count 1
                (byte)orientation, (byte)(orientation >> 8), 0x00, 0x00,  // value
                0x00, 0x00, 0x00, 0x00                                    // no next IFD
            };
            if (marker is not null)
            {
                tiff.AddRange(Encoding.ASCII.GetBytes(marker));
            }

            var payload = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff).ToArray();
            var length = payload.Length + 2;
            var segment = new byte[] { 0xFF, 0xE1, (byte)(length >> 8), (byte)length }.Concat(payload);
            return jpeg.Take(2).Concat(segment).Concat(jpeg.Skip(2)).ToArray();
        }

        // Colour of the pixel at (x, y) of an encoded image.
        public static SKColor PixelAt(byte[] image, int x, int y)
        {
            using var bitmap = SKBitmap.Decode(image);
            return bitmap.GetPixel(x, y);
        }

        public static (int Width, int Height) Size(byte[] image)
        {
            using var codec = SKCodec.Create(new SKMemoryStream(image));
            return (codec.Info.Width, codec.Info.Height);
        }

        // Left half one colour, right half another: shows where the left edge went after orientation.
        public static byte[] SplitJpeg(SKColor left, SKColor right, int width, int height)
        {
            using var bitmap = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(right);
                using var paint = new SKPaint { Color = left };
                canvas.DrawRect(0, 0, width / 2f, height, paint);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
            return data.ToArray();
        }

        private static byte[] Encode(SKColor colour, int width, int height, SKEncodedImageFormat format)
        {
            using var bitmap = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(colour);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(format, 95);
            return data.ToArray();
        }
    }
}
