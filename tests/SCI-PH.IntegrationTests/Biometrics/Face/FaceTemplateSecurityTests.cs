using System.Security.Cryptography;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Infrastructure.Biometrics.Face.OpenCv;

namespace SCI_PH.IntegrationTests.Biometrics.Face
{
    // P0 security of the local face provider's templates (ADR-012, PR 2), without OpenCV or a store: the strict plaintext
    // format, the AES-256-GCM envelope bound to its template id, key rotation, the strict subject reference, the key
    // configuration rules and the quality gate.
    public class FaceTemplateSecurityTests
    {
        private static readonly byte[] KeyOne = RandomNumberGenerator.GetBytes(32);
        private static readonly byte[] KeyTwo = RandomNumberGenerator.GetBytes(32);

        private static FaceTemplateCipher Cipher(string active = "k1") =>
            new(active, new Dictionary<string, byte[]> { ["k1"] = KeyOne, ["k2"] = KeyTwo });

        private static float[] Embedding(int seed = 1)
        {
            var random = new Random(seed);
            var values = Enumerable.Range(0, FaceModels.EmbeddingDimensions).Select(_ => (float)random.NextDouble() - 0.5f).ToArray();
            FaceQuality.Normalize(values);
            return values;
        }

        private static byte[] Template(int seed = 1) => FaceTemplateCodec.Serialize(FaceModels.AlgorithmVersion, Embedding(seed));

        [Fact]
        public void ATemplateRoundTripsThroughTheEnvelope()
        {
            var id = Guid.NewGuid();
            var envelope = Cipher().Encrypt(id, Template());

            var embedding = FaceTemplateCodec.Deserialize(Cipher().Decrypt(id, envelope), FaceModels.AlgorithmVersion, FaceModels.EmbeddingDimensions);

            Assert.Equal(Embedding(), embedding);
            Assert.Equal("SFE"u8.ToArray(), envelope[..3]);
            Assert.Equal(1, envelope[3]);
        }

        // A random nonce per encryption, and the plaintext floats never appear in the envelope.
        [Fact]
        public void EveryEncryptionIsDifferentAndOpaque()
        {
            var id = Guid.NewGuid();
            var plaintext = Template();
            var first = Cipher().Encrypt(id, plaintext);
            var second = Cipher().Encrypt(id, plaintext);

            Assert.NotEqual(first, second);
            // "SFE" | 1 | 2 | "k1" | nonce
            Assert.NotEqual(first.AsSpan(7, 12).ToArray(), second.AsSpan(7, 12).ToArray());
            Assert.Equal(-1, first.AsSpan().IndexOf(plaintext.AsSpan(plaintext.Length - 16)));
        }

        // The template id is authenticated: a ciphertext copied under another id never decrypts.
        [Fact]
        public void AnEnvelopeMovedToAnotherTemplateIdFails()
        {
            var envelope = Cipher().Encrypt(Guid.NewGuid(), Template());

            var failure = Assert.Throws<FaceTemplateUnavailableException>(() => Cipher().Decrypt(Guid.NewGuid(), envelope));
            Assert.Equal("FaceTemplateAuthenticationFailed", failure.Category);
        }

        // Any single changed byte (header, key id, nonce, tag or ciphertext) makes the envelope unusable.
        [Fact]
        public void EveryTamperedByteIsRejected()
        {
            var id = Guid.NewGuid();
            var envelope = Cipher().Encrypt(id, Template());
            for (var i = 0; i < envelope.Length; i++)
            {
                var tampered = envelope.ToArray();
                tampered[i] ^= 0x01;
                Assert.Throws<FaceTemplateUnavailableException>(() => Cipher().Decrypt(id, tampered));
            }

            Assert.Throws<FaceTemplateUnavailableException>(() => Cipher().Decrypt(id, envelope[..^1]));
            Assert.Throws<FaceTemplateUnavailableException>(() => Cipher().Decrypt(id, [.. envelope, 0]));
            Assert.Throws<FaceTemplateUnavailableException>(() => Cipher().Decrypt(id, []));
        }

        // Rotation: new templates use the active key; the older key still reads its templates; an unknown key id fails.
        [Fact]
        public void KeysRotateWithoutReencryptingAndUnknownKeysFail()
        {
            var id = Guid.NewGuid();
            var old = Cipher("k1").Encrypt(id, Template());
            var rotated = Cipher("k2");

            Assert.Equal("k2", System.Text.Encoding.ASCII.GetString(rotated.Encrypt(id, Template()).AsSpan(5, 2)));
            Assert.Equal(Template(), rotated.Decrypt(id, old));

            var withoutOld = new FaceTemplateCipher("k2", new Dictionary<string, byte[]> { ["k2"] = KeyTwo });
            Assert.Equal("FaceTemplateKeyUnknown", Assert.Throws<FaceTemplateUnavailableException>(() => withoutOld.Decrypt(id, old)).Category);

            var sameIdOtherKey = new FaceTemplateCipher("k1", new Dictionary<string, byte[]> { ["k1"] = KeyTwo });
            Assert.Equal("FaceTemplateAuthenticationFailed",
                Assert.Throws<FaceTemplateUnavailableException>(() => sameIdOtherKey.Decrypt(id, old)).Category);
        }

        [Fact]
        public void TheCipherRefusesKeysThatAreNotAes256()
        {
            Assert.Throws<ArgumentException>(() => new FaceTemplateCipher("k1", new Dictionary<string, byte[]> { ["k1"] = new byte[16] }));
            Assert.Throws<ArgumentException>(() => new FaceTemplateCipher("k3", new Dictionary<string, byte[]> { ["k1"] = KeyOne }));
        }

        // The plaintext format is read strictly: magic, version, algorithm, dimensions and exact length.
        [Fact]
        public void ThePlaintextFormatIsStrict()
        {
            var template = Template();
            float[] Read(byte[] bytes) => FaceTemplateCodec.Deserialize(bytes, FaceModels.AlgorithmVersion, FaceModels.EmbeddingDimensions);

            var magic = template.ToArray();
            magic[0] = (byte)'X';
            Assert.Equal("FaceTemplateFormatInvalid", Assert.Throws<FaceTemplateUnavailableException>(() => Read(magic)).Category);

            var version = template.ToArray();
            version[3] = 2;
            Assert.Equal("FaceTemplateFormatUnsupported", Assert.Throws<FaceTemplateUnavailableException>(() => Read(version)).Category);

            var otherAlgorithm = FaceTemplateCodec.Serialize("opencv-sface-2099", Embedding());
            Assert.Equal("FaceTemplateAlgorithmUnsupported", Assert.Throws<FaceTemplateUnavailableException>(() => Read(otherAlgorithm)).Category);

            var fewer = FaceTemplateCodec.Serialize(FaceModels.AlgorithmVersion, Embedding()[..127]);
            Assert.Equal("FaceTemplateDimensionsInvalid", Assert.Throws<FaceTemplateUnavailableException>(() => Read(fewer)).Category);

            Assert.Equal("FaceTemplateDimensionsInvalid", Assert.Throws<FaceTemplateUnavailableException>(() => Read([.. template, 0])).Category);
            Assert.Throws<FaceTemplateUnavailableException>(() => Read(template[..^4]));
            Assert.Throws<FaceTemplateUnavailableException>(() => Read([]));
        }

        [Fact]
        public void SubjectReferencesAreParsedStrictly()
        {
            var id = Guid.NewGuid();
            Assert.True(FaceSubjectReference.TryParse(FaceSubjectReference.Format(id), out var parsed));
            Assert.Equal(id, parsed);

            foreach (var invalid in new[]
            {
                null, "", "face:", $"FACE:{id:D}", $"face:{id:N}", $"face:{id:B}", $"face:{id.ToString("D").ToUpperInvariant()}",
                $" face:{id:D}", $"face:{id:D} ", $"face:{id:D}/x", $"r307:{id:D}", "face:../../etc/passwd"
            })
            {
                Assert.False(FaceSubjectReference.TryParse(invalid, out _), invalid);
            }
        }

        // Key configuration is checked before the host starts; the messages never contain a key.
        [Fact]
        public void KeyConfigurationProblemsAreReportedWithoutTheKeys()
        {
            static List<string> Problems(string? active, params (string Id, string Value)[] keys) =>
                OpenCvFaceSetup.KeyProblems(new OpenCvFaceOptions.EncryptionOptions
                {
                    ActiveKeyId = active,
                    Keys = keys.ToDictionary(k => k.Id, k => k.Value)
                }).ToList();

            var valid = Convert.ToBase64String(KeyOne);
            Assert.Empty(Problems("k1", ("k1", valid)));
            Assert.Empty(Problems("k2", ("k1", valid), ("k2", Convert.ToBase64String(KeyTwo))));

            Assert.NotEmpty(Problems(null, ("k1", valid)));
            Assert.NotEmpty(Problems("k2", ("k1", valid)));
            Assert.NotEmpty(Problems("k1"));
            var shortKey = Convert.ToBase64String(new byte[31]);
            var longKey = Convert.ToBase64String(new byte[33]);
            foreach (var problems in new[]
            {
                Problems("k1", ("k1", shortKey)), Problems("k1", ("k1", longKey)), Problems("k1", ("k1", "not base64!")),
                Problems("k1", ("k1", valid), ("bad id", valid))
            })
            {
                Assert.NotEmpty(problems);
                Assert.All(problems, p => Assert.DoesNotContain(shortKey, p));
                Assert.All(problems, p => Assert.DoesNotContain(longKey, p));
                Assert.All(problems, p => Assert.DoesNotContain(valid, p));
            }
        }

        // The quality gate of ADR-012 PR 2, without OpenCV.
        [Fact]
        public void TheQualityGateDecidesWhichFaceIsUsable()
        {
            var options = new OpenCvFaceOptions();
            FaceVerificationOutcome? Assess(params DetectedFace[] faces) => FaceQuality.Assess(faces, 640, 480, options, out _);
            DetectedFace Face(float score, float x = 200, float y = 100, float side = 160) => new(x, y, side, side, score);

            Assert.Equal(FaceVerificationOutcome.NoFace, Assess());
            Assert.Equal(FaceVerificationOutcome.NoFace, Assess(Face(0.59f)));
            Assert.Equal(FaceVerificationOutcome.LowQuality, Assess(Face(0.6f)));
            Assert.Equal(FaceVerificationOutcome.LowQuality, Assess(Face(0.89f)));
            Assert.Null(Assess(Face(0.9f)));
            Assert.Null(Assess(Face(0.95f), Face(0.7f, x: 400)));
            Assert.Equal(FaceVerificationOutcome.MultipleFaces, Assess(Face(0.95f), Face(0.91f, x: 400)));
            Assert.Equal(FaceVerificationOutcome.LowQuality, Assess(Face(0.95f, side: 79)));
            Assert.Null(Assess(Face(0.95f, side: 80)));
            // 10% of the box outside the image is accepted; more is not.
            Assert.Null(Assess(Face(0.95f, x: -16)));
            Assert.Equal(FaceVerificationOutcome.LowQuality, Assess(Face(0.95f, x: -17)));
            Assert.Equal(FaceVerificationOutcome.LowQuality, Assess(Face(0.95f, y: 480 - 140)));

            Assert.True(FaceQuality.Assess([Face(0.7f), Face(0.95f, x: 400)], 640, 480, options, out var best) is null && best == 1);
        }

        [Fact]
        public void EmbeddingsAreComparedByCosineOfUnitVectors()
        {
            var a = Embedding(1);
            Assert.Equal(1, FaceQuality.Cosine(a, a), 5);
            Assert.Equal(-1, FaceQuality.Cosine(a, a.Select(v => -v).ToArray()), 5);
            Assert.Throws<FaceTemplateUnavailableException>(() => FaceQuality.Cosine(a, a[..64]));
            Assert.Throws<InvalidOperationException>(() => FaceQuality.Normalize(new float[128]));
        }
    }
}
