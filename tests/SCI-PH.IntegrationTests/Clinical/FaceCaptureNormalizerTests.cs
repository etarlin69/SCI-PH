using System.Text;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Infrastructure.Biometrics.Face;
using SCI_PH.IntegrationTests.Clinical.Support;
using SkiaSharp;

namespace SCI_PH.IntegrationTests.Clinical
{
    // The capture input policy, independent of any face provider (ADR-012): size, real decoding, dimensions, orientation,
    // metadata removal and the normalized PNG every provider receives.
    public sealed class FaceCaptureNormalizerTests
    {
        private static readonly SkiaFaceCaptureNormalizer Normalizer = new();

        private static Task<SCI_PH.Application.Abstractions.Biometrics.FaceCapture> NormalizeAsync(byte[] bytes) =>
            Normalizer.NormalizeAsync(new MemoryStream(bytes), bytes.Length, CancellationToken.None);

        [Fact]
        public async Task AJpegOrPngBecomesAnUprightPngWithoutMetadata()
        {
            foreach (var input in new[] { FaceFixtures.Jpeg(FaceFixtures.FaceA), FaceFixtures.Png(FaceFixtures.FaceA) })
            {
                var capture = await NormalizeAsync(input);

                Assert.Equal((640, 480), (capture.Width, capture.Height));
                Assert.Equal([0x89, 0x50, 0x4E, 0x47], capture.Image.Take(4));
                Assert.Equal(64, capture.ContentHash.Length);
                Assert.Equal("FaceCapture(640x480)", capture.ToString());
            }
        }

        [Fact]
        public async Task TheSameImageAlwaysNormalizesToTheSameBytes()
        {
            var first = await NormalizeAsync(FaceFixtures.Png(FaceFixtures.FaceB));
            var second = await NormalizeAsync(FaceFixtures.Png(FaceFixtures.FaceB));

            Assert.Equal(first.ContentHash, second.ContentHash);
            Assert.NotEqual(first.ContentHash, (await NormalizeAsync(FaceFixtures.Png(FaceFixtures.FaceA))).ContentHash);
        }

        // EXIF orientation 6 (rotate 90° clockwise): 640x480 becomes 480x640 and the left edge ends at the top. The EXIF
        // segment, and anything in it, is gone.
        [Fact]
        public async Task TheExifOrientationIsAppliedAndTheMetadataRemoved()
        {
            var left = new SKColor(250, 250, 250);
            var right = new SKColor(20, 40, 160);
            var source = FaceFixtures.SplitJpeg(left, right, 640, 480);
            var exif = InsertExif(source, 6, "SECRET-GPS-18.47N");

            var capture = await NormalizeAsync(exif);

            Assert.Equal((480, 640), (capture.Width, capture.Height));
            Assert.True(Close(left, FaceFixtures.PixelAt(capture.Image, 240, 20)), "the left edge must be on top after a 90° rotation");
            Assert.True(Close(right, FaceFixtures.PixelAt(capture.Image, 240, 620)));
            var text = Encoding.ASCII.GetString(capture.Image);
            Assert.DoesNotContain("SECRET-GPS", text);
            Assert.DoesNotContain("Exif", text);
        }

        [Fact]
        public async Task ALargeCaptureIsScaledDownToTheNormalizedSize()
        {
            var capture = await NormalizeAsync(FaceFixtures.Jpeg(FaceFixtures.FaceA, 2048, 1536));

            Assert.Equal((1024, 768), (capture.Width, capture.Height));
        }

        public static TheoryData<string, byte[]> InvalidCaptures => new()
        {
            { "empty", [] },
            { "text with a JPEG name", Encoding.UTF8.GetBytes("this is not an image at all") },
            { "JPEG magic, garbage after", [0xFF, 0xD8, 0xFF, 0xE0, .. Enumerable.Repeat((byte)0x42, 4000)] },
            { "PNG magic, garbage after", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Repeat((byte)0x13, 4000)] },
            { "GIF", [.. Encoding.ASCII.GetBytes("GIF89a"), .. Enumerable.Repeat((byte)0, 100)] },
            { "too small", FaceFixtures.Png(FaceFixtures.FaceA, 320, 240) },
            { "too large a side", FaceFixtures.Png(FaceFixtures.FaceA, 5000, 600) },
            { "over 2 MB", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[SkiaFaceCaptureNormalizer.MaxBytes]] }
        };

        [Theory]
        [MemberData(nameof(InvalidCaptures))]
        public async Task AnythingElseIsRejectedAsInvalidInput(string name, byte[] bytes)
        {
            var error = await Assert.ThrowsAsync<RequestValidationException>(() => NormalizeAsync(bytes));

            Assert.Equal("capture", error.Field);
            Assert.False(string.IsNullOrEmpty(name));
        }

        private static byte[] InsertExif(byte[] jpeg, ushort orientation, string marker)
        {
            var donor = FaceFixtures.JpegWithExif(FaceFixtures.FaceA, 8, 8, orientation, marker);
            // The APP1 segment the donor carries right after its SOI.
            var length = (donor[4] << 8) | donor[5];
            var segment = donor.Skip(2).Take(length + 2);
            return jpeg.Take(2).Concat(segment).Concat(jpeg.Skip(2)).ToArray();
        }

        private static bool Close(SKColor expected, SKColor actual) =>
            Math.Abs(expected.Red - actual.Red) < 24 && Math.Abs(expected.Green - actual.Green) < 24 && Math.Abs(expected.Blue - actual.Blue) < 24;
    }
}
