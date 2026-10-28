using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SCI_PH.Infrastructure.Biometrics.Face.OpenCv
{
    // A template of the local face provider could not be used (missing, corrupt, of another key, format or algorithm). Always
    // a provider failure (503), never a NoMatch. Category: a safe label for the logs; never template or ciphertext bytes.
    internal sealed class FaceTemplateUnavailableException(string category) : Exception(category)
    {
        public string Category { get; } = category;
    }

    // "face:<GUID in D format>", strictly: the key of the template in the provider's store. Never a URL, a container, a key id,
    // an algorithm or patient data.
    internal static class FaceSubjectReference
    {
        private const string Prefix = "face:";

        public static string Format(Guid templateId) => $"{Prefix}{templateId:D}";

        public static bool TryParse(string? subjectReference, out Guid templateId)
        {
            templateId = Guid.Empty;
            return subjectReference is not null
                && subjectReference.StartsWith(Prefix, StringComparison.Ordinal)
                && Guid.TryParseExact(subjectReference.AsSpan(Prefix.Length), "D", out templateId)
                && string.Equals(Format(templateId), subjectReference, StringComparison.Ordinal);
        }
    }

    // Plaintext template: "SFT" | formatVersion u8 = 1 | algorithmVersion length u8 | algorithmVersion UTF-8 | dimensions u16 LE
    // | float32[dimensions] LE. Deterministic and versioned; no patient, key or location inside. Read strictly.
    internal static class FaceTemplateCodec
    {
        public const byte FormatVersion = 1;
        private static readonly byte[] Magic = "SFT"u8.ToArray();

        public static byte[] Serialize(string algorithmVersion, ReadOnlySpan<float> embedding)
        {
            var algorithm = Encoding.UTF8.GetBytes(algorithmVersion);
            if (algorithm.Length is 0 or > byte.MaxValue || embedding.Length is 0 or > ushort.MaxValue)
            {
                throw new ArgumentException("The face template header is out of range.");
            }

            var bytes = new byte[Magic.Length + 1 + 1 + algorithm.Length + 2 + embedding.Length * 4];
            var span = bytes.AsSpan();
            Magic.CopyTo(span);
            var offset = Magic.Length;
            span[offset++] = FormatVersion;
            span[offset++] = (byte)algorithm.Length;
            algorithm.CopyTo(span[offset..]);
            offset += algorithm.Length;
            BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)embedding.Length);
            offset += 2;
            foreach (var value in embedding)
            {
                BinaryPrimitives.WriteSingleLittleEndian(span[offset..], value);
                offset += 4;
            }

            return bytes;
        }

        // The embedding, only if the bytes are exactly a template of this format, algorithm and dimensions.
        public static float[] Deserialize(ReadOnlySpan<byte> bytes, string expectedAlgorithmVersion, int expectedDimensions)
        {
            if (bytes.Length < Magic.Length + 4 || !bytes[..Magic.Length].SequenceEqual(Magic))
            {
                throw new FaceTemplateUnavailableException("FaceTemplateFormatInvalid");
            }

            var offset = Magic.Length;
            if (bytes[offset++] != FormatVersion)
            {
                throw new FaceTemplateUnavailableException("FaceTemplateFormatUnsupported");
            }

            int algorithmLength = bytes[offset++];
            if (bytes.Length < offset + algorithmLength + 2)
            {
                throw new FaceTemplateUnavailableException("FaceTemplateFormatInvalid");
            }

            var algorithm = Encoding.UTF8.GetString(bytes.Slice(offset, algorithmLength));
            offset += algorithmLength;
            if (!string.Equals(algorithm, expectedAlgorithmVersion, StringComparison.Ordinal))
            {
                throw new FaceTemplateUnavailableException("FaceTemplateAlgorithmUnsupported");
            }

            int dimensions = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
            offset += 2;
            if (dimensions != expectedDimensions || bytes.Length != offset + dimensions * 4)
            {
                throw new FaceTemplateUnavailableException("FaceTemplateDimensionsInvalid");
            }

            var embedding = new float[dimensions];
            for (var i = 0; i < dimensions; i++)
            {
                embedding[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset + i * 4, 4));
            }

            return embedding;
        }
    }

    // Encrypted envelope: "SFE" | envelopeVersion u8 = 1 | keyId length u8 | keyId ASCII | nonce[12] | tag[16] | ciphertext.
    // AES-256-GCM with a cryptographically random nonce per encryption. The associated data is the envelope header AND the
    // template GUID, so a ciphertext copied under another template id, or with an edited header, fails authentication. The
    // key never travels with it; any configured key can decrypt, only the active one encrypts.
    internal sealed class FaceTemplateCipher
    {
        public const byte EnvelopeVersion = 1;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private static readonly byte[] Magic = "SFE"u8.ToArray();

        private readonly string _activeKeyId;
        private readonly IReadOnlyDictionary<string, byte[]> _keys;

        public FaceTemplateCipher(string activeKeyId, IReadOnlyDictionary<string, byte[]> keys)
        {
            if (!keys.TryGetValue(activeKeyId, out _) || keys.Values.Any(k => k.Length != 32))
            {
                throw new ArgumentException("The face template keys are not valid.");
            }

            _activeKeyId = activeKeyId;
            _keys = keys;
        }

        public byte[] Encrypt(Guid templateId, ReadOnlySpan<byte> plaintext)
        {
            var header = Header(_activeKeyId);
            var envelope = new byte[header.Length + NonceSize + TagSize + plaintext.Length];
            header.CopyTo(envelope, 0);
            var nonce = envelope.AsSpan(header.Length, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            var tag = envelope.AsSpan(header.Length + NonceSize, TagSize);
            var ciphertext = envelope.AsSpan(header.Length + NonceSize + TagSize);
            using var aes = new AesGcm(_keys[_activeKeyId], TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(header, templateId));
            return envelope;
        }

        // The plaintext template, only if the envelope authenticates for this template id with a configured key. The caller
        // clears the returned buffer.
        public byte[] Decrypt(Guid templateId, ReadOnlySpan<byte> envelope)
        {
            if (envelope.Length < Magic.Length + 2 || !envelope[..Magic.Length].SequenceEqual(Magic))
            {
                throw new FaceTemplateUnavailableException("FaceTemplateEnvelopeInvalid");
            }

            if (envelope[Magic.Length] != EnvelopeVersion)
            {
                throw new FaceTemplateUnavailableException("FaceTemplateEnvelopeUnsupported");
            }

            int keyIdLength = envelope[Magic.Length + 1];
            var headerLength = Magic.Length + 2 + keyIdLength;
            if (envelope.Length < headerLength + NonceSize + TagSize)
            {
                throw new FaceTemplateUnavailableException("FaceTemplateEnvelopeInvalid");
            }

            var keyId = Encoding.ASCII.GetString(envelope.Slice(Magic.Length + 2, keyIdLength));
            if (!_keys.TryGetValue(keyId, out var key))
            {
                throw new FaceTemplateUnavailableException("FaceTemplateKeyUnknown");
            }

            var header = envelope[..headerLength];
            var plaintext = new byte[envelope.Length - headerLength - NonceSize - TagSize];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(
                    envelope.Slice(headerLength, NonceSize),
                    envelope[(headerLength + NonceSize + TagSize)..],
                    envelope.Slice(headerLength + NonceSize, TagSize),
                    plaintext,
                    AssociatedData(header, templateId));
                return plaintext;
            }
            catch (AuthenticationTagMismatchException)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                throw new FaceTemplateUnavailableException("FaceTemplateAuthenticationFailed");
            }
        }

        private static byte[] Header(string keyId)
        {
            var id = Encoding.ASCII.GetBytes(keyId);
            var header = new byte[Magic.Length + 2 + id.Length];
            Magic.CopyTo(header, 0);
            header[Magic.Length] = EnvelopeVersion;
            header[Magic.Length + 1] = (byte)id.Length;
            id.CopyTo(header, Magic.Length + 2);
            return header;
        }

        private static byte[] AssociatedData(ReadOnlySpan<byte> header, Guid templateId) => [.. header, .. templateId.ToByteArray()];
    }
}
