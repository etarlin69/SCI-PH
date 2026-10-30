using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SCI_PH.Infrastructure.Biometrics.Face.OpenCv
{
    // Fail-fast configuration of the local face provider (ADR-012, PR 2): with Provider=OpenCv the host does not start on a
    // missing model, a model whose SHA-256 differs from the pinned one, a missing or malformed key, or no template store.
    // A store that is configured but down at runtime is NOT checked here: that is a 503 per request. Messages never contain a
    // key, a connection string or a full path.
    internal sealed partial class OpenCvFaceOptionsValidator : IValidateOptions<OpenCvFaceOptions>
    {
        private readonly IHostEnvironment _environment;

        public OpenCvFaceOptionsValidator(IHostEnvironment environment) => _environment = environment;

        public ValidateOptionsResult Validate(string? name, OpenCvFaceOptions options)
        {
            var failures = new List<string>();
            CheckModel(OpenCvFaceSetup.Resolve(_environment, options.YuNetModelPath), FaceModels.YuNetSha256, "YuNetModelPath", failures);
            CheckModel(OpenCvFaceSetup.Resolve(_environment, options.SFaceModelPath), FaceModels.SFaceSha256, "SFaceModelPath", failures);

            if (options.CosineThreshold is <= 0 or >= 1)
            {
                failures.Add($"{OpenCvFaceOptions.SectionName}:CosineThreshold must be between 0 and 1.");
            }

            if (options.DetectorScore is <= 0 or > 1 || options.GoodScore is <= 0 or > 1 || options.DetectorScore > options.GoodScore)
            {
                failures.Add($"{OpenCvFaceOptions.SectionName}: DetectorScore and GoodScore must be in (0, 1] with DetectorScore <= GoodScore.");
            }

            if (options.MinFaceSide < 1 || options.EngineCapacity < 0)
            {
                failures.Add($"{OpenCvFaceOptions.SectionName}: MinFaceSide must be positive and EngineCapacity not negative.");
            }

            if (string.IsNullOrWhiteSpace(options.Templates.ConnectionString))
            {
                failures.Add($"{OpenCvFaceOptions.SectionName}:Templates:ConnectionString is required (a secret: User Secrets or environment).");
            }

            if (!ContainerName().IsMatch(options.Templates.Container ?? ""))
            {
                failures.Add($"{OpenCvFaceOptions.SectionName}:Templates:Container is not a valid private container name.");
            }

            if (options.Cleanup.IntervalSeconds < 5 || options.Cleanup.BatchSize is < 1 or > 500)
            {
                failures.Add($"{OpenCvFaceOptions.SectionName}:Cleanup: IntervalSeconds must be at least 5 and BatchSize between 1 and 500.");
            }

            failures.AddRange(OpenCvFaceSetup.KeyProblems(options.Encryption));
            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }

        private static void CheckModel(string path, string expectedSha256, string setting, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add($"{OpenCvFaceOptions.SectionName}:{setting}: model {Path.GetFileName(path)} not found. Run scripts/biometrics/fetch-face-models.");
                return;
            }

            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
            {
                failures.Add($"{OpenCvFaceOptions.SectionName}:{setting}: model {Path.GetFileName(path)} does not have the pinned SHA-256.");
            }
        }

        [GeneratedRegex("^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$")]
        private static partial Regex ContainerName();
    }

    internal static partial class OpenCvFaceSetup
    {
        public static string Resolve(IHostEnvironment environment, string path) =>
            Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path);

        public static IEnumerable<string> KeyProblems(OpenCvFaceOptions.EncryptionOptions encryption)
        {
            var prefix = $"{OpenCvFaceOptions.SectionName}:Encryption";
            if (string.IsNullOrWhiteSpace(encryption.ActiveKeyId))
            {
                yield return $"{prefix}:ActiveKeyId is required.";
            }
            else if (!encryption.Keys.ContainsKey(encryption.ActiveKeyId))
            {
                yield return $"{prefix}:Keys has no key for the ActiveKeyId.";
            }

            foreach (var (keyId, value) in encryption.Keys)
            {
                if (!KeyId().IsMatch(keyId))
                {
                    yield return $"{prefix}:Keys: a key id is not valid (1-64 of A-Z a-z 0-9 . _ -).";
                }
                else if (DecodeKey(value) is not { Length: 32 } key)
                {
                    yield return $"{prefix}:Keys:{keyId} must be base64 of exactly 32 bytes.";
                }
                else
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }
        }

        // Only after validation: keyId -> 32-byte key.
        public static Dictionary<string, byte[]> LoadKeys(OpenCvFaceOptions.EncryptionOptions encryption) =>
            encryption.Keys.ToDictionary(k => k.Key, k => DecodeKey(k.Value)!, StringComparer.Ordinal);

        private static byte[]? DecodeKey(string? value)
        {
            var buffer = new byte[64];
            try
            {
                return value is not null && Convert.TryFromBase64String(value.Trim(), buffer, out var written) ? buffer[..written] : null;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }

        [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
        private static partial Regex KeyId();
    }
}
