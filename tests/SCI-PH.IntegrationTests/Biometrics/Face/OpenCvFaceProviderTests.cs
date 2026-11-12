using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Infrastructure;
using SCI_PH.Infrastructure.Biometrics.Face;
using SCI_PH.Infrastructure.Biometrics.Face.OpenCv;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Biometrics.Face
{
    // The local face provider (LocalOpenCvFaceVerificationService, ADR-012 PR 2) around a stub analyzer and an in-memory
    // store: what it stores, how it decides, and that every template, store or reference problem is a 503 (never NoMatch)
    // with safe logs. Also the native OpenCV runtime load (normal CI, no models) and the fail-fast configuration.
    public class OpenCvFaceProviderTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
        private static readonly FaceCapture Capture = new([1, 2, 3], 640, 480, "hash");

        private sealed class Provider
        {
            public InMemoryTemplateStore Store { get; } = new();
            public StubAnalyzer Analyzer { get; } = new();
            public CapturingLoggerProvider Logs { get; } = new();
            public FaceTemplateCipher Cipher { get; } = new("k1", new Dictionary<string, byte[]> { ["k1"] = Key });
            public LocalOpenCvFaceVerificationService Service { get; }

            public Provider()
            {
                var logger = LoggerFactory.Create(b => b.AddProvider(Logs)).CreateLogger<LocalOpenCvFaceVerificationService>();
                Service = new LocalOpenCvFaceVerificationService(Analyzer, Store, Cipher, new OpenCvFaceOptions(), logger);
            }

            public async Task<string> EnrollAsync(float[] embedding)
            {
                Analyzer.Next = new FaceAnalysis(null, embedding.ToArray());
                var result = await Service.EnrollAsync(Capture, None);
                Assert.Equal(FaceEnrollmentOutcome.Enrolled, result.Outcome);
                return result.SubjectReference!;
            }

            public Task<FaceVerificationOutcome> VerifyAsync(string subject, float[] probe)
            {
                Analyzer.Next = new FaceAnalysis(null, probe.ToArray());
                return Service.VerifyAsync(subject, Capture, None);
            }
        }

        internal sealed class StubAnalyzer : IFaceAnalyzer
        {
            public FaceAnalysis Next { get; set; }
            public int Calls { get; private set; }

            public Task<FaceAnalysis> AnalyzeAsync(byte[] image, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Next);
            }
        }

        internal sealed class InMemoryTemplateStore : IFaceTemplateStore
        {
            public Dictionary<Guid, byte[]> Blobs { get; } = [];
            public bool Down { get; set; }

            public Task PutAsync(Guid templateId, byte[] envelope, CancellationToken cancellationToken)
            {
                Fail();
                Blobs.Add(templateId, envelope);
                return Task.CompletedTask;
            }

            public Task<byte[]?> GetAsync(Guid templateId, CancellationToken cancellationToken)
            {
                Fail();
                return Task.FromResult(Blobs.TryGetValue(templateId, out var blob) ? blob : null);
            }

            public Task<bool> DeleteAsync(Guid templateId, CancellationToken cancellationToken)
            {
                Fail();
                return Task.FromResult(Blobs.Remove(templateId));
            }

            public async IAsyncEnumerable<Guid> ListAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                Fail();
                foreach (var id in Blobs.Keys.ToList())
                {
                    await Task.Yield();
                    yield return id;
                }
            }

            private void Fail()
            {
                if (Down)
                {
                    throw new InvalidOperationException("https://account.blob.core.windows.net/face-templates is down");
                }
            }
        }

        // Unit vector at cosine `similarity` from `basis` (which is e0 here).
        private static float[] Unit(int axis) => Enumerable.Range(0, 128).Select(i => i == axis ? 1f : 0f).ToArray();

        private static float[] At(double similarity)
        {
            var vector = new float[128];
            vector[0] = (float)similarity;
            vector[1] = (float)Math.Sqrt(1 - similarity * similarity);
            return vector;
        }

        [Fact]
        public async Task EnrollmentStoresOnlyAnEncryptedTemplateUnderAnOpaqueReference()
        {
            var p = new Provider();

            var subject = await p.EnrollAsync(Unit(0));

            Assert.True(FaceSubjectReference.TryParse(subject, out var id));
            var blob = Assert.Single(p.Store.Blobs);
            Assert.Equal(id, blob.Key);
            Assert.Equal("SFE"u8.ToArray(), blob.Value[..3]);
            var plaintext = p.Cipher.Decrypt(id, blob.Value);
            Assert.Equal(Unit(0), FaceTemplateCodec.Deserialize(plaintext, FaceModels.AlgorithmVersion, 128));
            Assert.Equal(("SCIPH-FaceOpenCV", FaceModels.AlgorithmVersion), (p.Service.Provider, p.Service.AlgorithmVersion));
        }

        [Fact]
        public async Task RejectedCapturesCreateNothing()
        {
            var p = new Provider();
            foreach (var (rejected, expected) in new[]
            {
                (FaceVerificationOutcome.NoFace, FaceEnrollmentOutcome.NoFace),
                (FaceVerificationOutcome.MultipleFaces, FaceEnrollmentOutcome.MultipleFaces),
                (FaceVerificationOutcome.LowQuality, FaceEnrollmentOutcome.LowQuality)
            })
            {
                p.Analyzer.Next = new FaceAnalysis(rejected, null);
                Assert.Equal(FaceEnrollmentResult.Rejected(expected), await p.Service.EnrollAsync(Capture, None));
            }

            Assert.Empty(p.Store.Blobs);
        }

        // Match at or over the server-side threshold (0.363), NoMatch under it; rejections pass through.
        [Fact]
        public async Task VerificationIsDecidedByTheServerSideThreshold()
        {
            var p = new Provider();
            var subject = await p.EnrollAsync(Unit(0));

            Assert.Equal(FaceVerificationOutcome.Match, await p.VerifyAsync(subject, Unit(0)));
            Assert.Equal(FaceVerificationOutcome.Match, await p.VerifyAsync(subject, At(0.364)));
            Assert.Equal(FaceVerificationOutcome.NoMatch, await p.VerifyAsync(subject, At(0.362)));
            Assert.Equal(FaceVerificationOutcome.NoMatch, await p.VerifyAsync(subject, Unit(1)));
            p.Analyzer.Next = new FaceAnalysis(FaceVerificationOutcome.MultipleFaces, null);
            Assert.Equal(FaceVerificationOutcome.MultipleFaces, await p.Service.VerifyAsync(subject, Capture, None));
        }

        // Template store down: enrollment and verification are 503 and nothing is written.
        [Fact]
        public async Task AStoreOutageIsUnavailable()
        {
            var p = new Provider();
            var subject = await p.EnrollAsync(Unit(0));
            p.Store.Down = true;

            p.Analyzer.Next = new FaceAnalysis(null, Unit(0));
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => p.Service.EnrollAsync(Capture, None));
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => p.VerifyAsync(subject, Unit(0)));
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => p.Service.DeleteAsync(subject, None));

            p.Store.Down = false;
            Assert.Single(p.Store.Blobs);
            Assert.Contains("FaceTemplateStoreUnavailable", p.Logs.Text);
            Assert.DoesNotContain("blob.core.windows.net", p.Logs.Text);
        }

        // A missing, corrupt, moved or foreign template, or a malformed reference, is a 503: never NoMatch, and a malformed
        // reference never reaches the store or the engine.
        [Fact]
        public async Task UnusableTemplatesAreUnavailableNeverNoMatch()
        {
            var p = new Provider();
            var subject = await p.EnrollAsync(Unit(0));
            FaceSubjectReference.TryParse(subject, out var id);

            var envelope = p.Store.Blobs[id];
            p.Store.Blobs.Remove(id);
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => p.VerifyAsync(subject, Unit(0)));
            Assert.Contains("FaceTemplateMissing", p.Logs.Text);

            var corrupt = envelope.ToArray();
            corrupt[^1] ^= 0xFF;
            p.Store.Blobs[id] = corrupt;
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => p.VerifyAsync(subject, Unit(0)));
            Assert.Contains("FaceTemplateAuthenticationFailed", p.Logs.Text);

            // The same ciphertext under another reference.
            var other = Guid.NewGuid();
            p.Store.Blobs[other] = envelope;
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => p.VerifyAsync(FaceSubjectReference.Format(other), Unit(0)));

            var calls = p.Analyzer.Calls;
            foreach (var invalid in new[] { "r307:1", "face:x", $"face:{id:N}", "" })
            {
                await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => p.VerifyAsync(invalid, Unit(0)));
                await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => p.Service.DeleteAsync(invalid, None));
            }

            Assert.Equal(calls, p.Analyzer.Calls);
            Assert.Contains("FaceReferenceInvalid", p.Logs.Text);
        }

        [Fact]
        public async Task DeletionReportsDeletedThenNotFound()
        {
            var p = new Provider();
            var subject = await p.EnrollAsync(Unit(0));

            Assert.Equal(FaceDeletionOutcome.Deleted, await p.Service.DeleteAsync(subject, None));
            Assert.Equal(FaceDeletionOutcome.NotFound, await p.Service.DeleteAsync(subject, None));
            Assert.Empty(p.Store.Blobs);
        }

        // Logs: safe categories only, never a reference, a template, a key or an embedding.
        [Fact]
        public async Task LogsCarryNoBiometricOrKeyMaterial()
        {
            var p = new Provider();
            var subject = await p.EnrollAsync(Unit(0));
            FaceSubjectReference.TryParse(subject, out var id);
            var envelope = p.Store.Blobs[id];
            p.Store.Blobs[id] = envelope[..^1];
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => p.VerifyAsync(subject, Unit(0)));
            p.Store.Blobs.Remove(id);
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => p.VerifyAsync(subject, Unit(0)));

            var logs = p.Logs.Text;
            Assert.NotEmpty(logs);
            foreach (var secret in new[] { subject, id.ToString(), Convert.ToBase64String(Key), Convert.ToBase64String(envelope)[..24], "0.363", "score" })
            {
                Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Normal CI (Windows and Ubuntu): the pinned native runtime loads and its face API is callable, without any model.
        [Fact]
        public void TheNativeOpenCvRuntimeLoads()
        {
            Assert.StartsWith("5.0.", Cv2.GetVersionString(), StringComparison.Ordinal);
            using var image = new Mat(new Size(64, 48), MatType.CV_8UC3, Scalar.All(127));
            Cv2.ImEncode(".png", image, out var png);
            using var decoded = Cv2.ImDecode(png, ImreadModes.Color);
            Assert.Equal((64, 48), (decoded.Width, decoded.Height));

            // The face API is present in the native library: a missing model is OpenCV's own error, not a missing entry point.
            var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.onnx");
            var failure = Record.Exception(() => FaceDetectorYN.Create(missing, "", new Size(320, 320)).Dispose());
            Assert.IsAssignableFrom<OpenCVException>(failure);
            failure = Record.Exception(() => FaceRecognizerSF.Create(missing, "").Dispose());
            Assert.IsAssignableFrom<OpenCVException>(failure);
        }

        private sealed class Environment(string name, string contentRoot) : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = name;
            public string ApplicationName { get; set; } = "SCI-PH.Tests";
            public string ContentRootPath { get; set; } = contentRoot;
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
                new Microsoft.Extensions.FileProviders.NullFileProvider();
        }

        private static ServiceProvider Build(IDictionary<string, string?> settings, string environment = "Production", string? contentRoot = null)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostEnvironment>(new Environment(environment, contentRoot ?? AppContext.BaseDirectory));
            services.AddFaceVerification(configuration);
            return services.BuildServiceProvider();
        }

        private static Dictionary<string, string?> OpenCvSettings(string modelDirectory, string key) => new()
        {
            ["FaceVerification:Provider"] = "OpenCv",
            ["FaceVerification:OpenCv:YuNetModelPath"] = Path.Combine(modelDirectory, FaceModels.YuNetFileName),
            ["FaceVerification:OpenCv:SFaceModelPath"] = Path.Combine(modelDirectory, FaceModels.SFaceFileName),
            ["FaceVerification:OpenCv:Templates:ConnectionString"] = "UseDevelopmentStorage=true",
            ["FaceVerification:OpenCv:Encryption:ActiveKeyId"] = "k1",
            ["FaceVerification:OpenCv:Encryption:Keys:k1"] = key
        };

        // Disabled: the host needs no model, key or store; every operation is a 503; no OpenCV service or worker exists.
        [Fact]
        public async Task TheDisabledProviderNeedsNothingAndAnswersUnavailable()
        {
            await using var provider = Build(new Dictionary<string, string?> { ["FaceVerification:Provider"] = "Disabled" });

            _ = provider.GetRequiredService<IOptions<FaceVerificationOptions>>().Value;
            var faces = provider.GetRequiredService<IFaceVerificationService>();
            Assert.IsType<DisabledFaceVerificationService>(faces);
            Assert.False(faces.IsEnabled);
            Assert.Empty(provider.GetServices<IHostedService>());
            Assert.Null(provider.GetService<LocalOpenCvFaceVerificationService>());
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => faces.DeleteAsync("face:x", None));
        }

        // OpenCv: missing models, models with another hash, missing or malformed keys, or no store stop the start, with
        // messages that never contain a key or the connection string.
        [Fact]
        public void TheOpenCvProviderFailsFastOnItsConfiguration()
        {
            var directory = Directory.CreateTempSubdirectory("sciph-face-models-").FullName;
            try
            {
                var key = Convert.ToBase64String(Key);
                using (var missing = Build(OpenCvSettings(directory, key)))
                {
                    var failure = Assert.Throws<OptionsValidationException>(() => missing.GetRequiredService<IOptions<OpenCvFaceOptions>>().Value);
                    Assert.Equal(2, failure.Failures.Count(f => f.Contains("not found", StringComparison.Ordinal)));
                    Assert.DoesNotContain(key, failure.Message);
                }

                File.WriteAllText(Path.Combine(directory, FaceModels.YuNetFileName), "not the model");
                File.WriteAllText(Path.Combine(directory, FaceModels.SFaceFileName), "not the model");
                var settings = OpenCvSettings(directory, Convert.ToBase64String(new byte[31]));
                settings["FaceVerification:OpenCv:Templates:ConnectionString"] = "";
                using var wrong = Build(settings);
                var failures = Assert.Throws<OptionsValidationException>(() => wrong.GetRequiredService<IOptions<OpenCvFaceOptions>>().Value).Failures.ToList();
                Assert.Equal(2, failures.Count(f => f.Contains("pinned SHA-256", StringComparison.Ordinal)));
                Assert.Contains(failures, f => f.Contains("exactly 32 bytes", StringComparison.Ordinal));
                Assert.Contains(failures, f => f.Contains("ConnectionString is required", StringComparison.Ordinal));
                Assert.DoesNotContain(failures, f => f.Contains(Convert.ToBase64String(new byte[31]), StringComparison.Ordinal));
                Assert.DoesNotContain(failures, f => f.Contains(directory, StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        // OpenCv is a real provider: allowed in Production (unlike the Fake), with its startup check and cleanup worker.
        [Fact]
        public void TheOpenCvProviderIsAllowedInProductionWithItsWorker()
        {
            var settings = OpenCvSettings(Path.GetTempPath(), Convert.ToBase64String(Key));
            using var provider = Build(settings);
            _ = provider.GetRequiredService<IOptions<FaceVerificationOptions>>().Value;

            // Registrations only: building them would load the (absent) models.
            var services = new ServiceCollection();
            services.AddFaceVerification(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            Assert.Equal(["OpenCvFaceProviderStartup", "FaceTemplateCleanupWorker"],
                services.Where(s => s.ServiceType == typeof(IHostedService)).Select(s => s.ImplementationType!.Name));
        }
    }
}
