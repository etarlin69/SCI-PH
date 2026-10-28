using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Errors;

namespace SCI_PH.Infrastructure.Biometrics.Face.OpenCv
{
    // Real local face provider (ADR-012, PR 2): YuNet detection + quality gate + SFace embedding, in-process (OpenCV, no
    // cloud). Its templates are encrypted (AES-256-GCM) and kept in its own store under an opaque GUID; the clinical schema only
    // has "face:<guid>". Match is decided here with the server-side cosine threshold; the score never leaves this class.
    // Every failure of the store, the keys, a template or the engine is a 503 (never NoMatch). Logs carry safe categories
    // only: never images, embeddings, scores, templates, keys or patient data.
    public sealed class LocalOpenCvFaceVerificationService : IFaceVerificationService
    {
        public const string ProviderName = "SCIPH-FaceOpenCV";

        private readonly IFaceAnalyzer _engines;
        private readonly IFaceTemplateStore _templates;
        private readonly FaceTemplateCipher _cipher;
        private readonly double _threshold;
        private readonly ILogger _logger;

        internal LocalOpenCvFaceVerificationService(
            IFaceAnalyzer engines, IFaceTemplateStore templates, FaceTemplateCipher cipher, OpenCvFaceOptions options,
            ILogger<LocalOpenCvFaceVerificationService> logger)
        {
            _engines = engines;
            _templates = templates;
            _cipher = cipher;
            _threshold = options.CosineThreshold;
            _logger = logger;
        }

        public string Provider => ProviderName;

        public string AlgorithmVersion => FaceModels.AlgorithmVersion;

        public async Task<FaceEnrollmentResult> EnrollAsync(FaceCapture capture, CancellationToken cancellationToken)
        {
            var analysis = await AnalyzeAsync(capture, cancellationToken);
            if (analysis.Rejected is { } rejected)
            {
                return FaceEnrollmentResult.Rejected(rejected switch
                {
                    FaceVerificationOutcome.NoFace => FaceEnrollmentOutcome.NoFace,
                    FaceVerificationOutcome.MultipleFaces => FaceEnrollmentOutcome.MultipleFaces,
                    _ => FaceEnrollmentOutcome.LowQuality
                });
            }

            var templateId = Guid.NewGuid();
            var embedding = analysis.Embedding!;
            byte[]? plaintext = null;
            try
            {
                plaintext = FaceTemplateCodec.Serialize(FaceModels.AlgorithmVersion, embedding);
                var envelope = _cipher.Encrypt(templateId, plaintext);
                await _templates.PutAsync(templateId, envelope, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Nothing clinical is written for a failed enrollment.
                throw Unavailable("FaceTemplateStoreUnavailable", exception);
            }
            finally
            {
                Clear(embedding);
                if (plaintext is not null)
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }

            return new FaceEnrollmentResult(FaceEnrollmentOutcome.Enrolled, FaceSubjectReference.Format(templateId));
        }

        public async Task<FaceVerificationOutcome> VerifyAsync(string subjectReference, FaceCapture capture, CancellationToken cancellationToken)
        {
            // The enrolled template first: a missing or unusable one is a 503 whatever the capture is.
            var enrolled = await LoadAsync(subjectReference, cancellationToken);
            try
            {
                var analysis = await AnalyzeAsync(capture, cancellationToken);
                if (analysis.Rejected is { } rejected)
                {
                    return rejected;
                }

                var probe = analysis.Embedding!;
                try
                {
                    return FaceQuality.Cosine(enrolled, probe) >= _threshold ? FaceVerificationOutcome.Match : FaceVerificationOutcome.NoMatch;
                }
                finally
                {
                    Clear(probe);
                }
            }
            finally
            {
                Clear(enrolled);
            }
        }

        public async Task<FaceDeletionOutcome> DeleteAsync(string subjectReference, CancellationToken cancellationToken)
        {
            var templateId = Parse(subjectReference);
            try
            {
                return await _templates.DeleteAsync(templateId, cancellationToken) ? FaceDeletionOutcome.Deleted : FaceDeletionOutcome.NotFound;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw Unavailable("FaceTemplateStoreUnavailable", exception);
            }
        }

        private async Task<float[]> LoadAsync(string subjectReference, CancellationToken cancellationToken)
        {
            var templateId = Parse(subjectReference);
            byte[]? envelope;
            try
            {
                envelope = await _templates.GetAsync(templateId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw Unavailable("FaceTemplateStoreUnavailable", exception);
            }

            if (envelope is null)
            {
                // An Active reference whose template is gone (reconciliation case A).
                throw Unavailable("FaceTemplateMissing");
            }

            byte[]? plaintext = null;
            try
            {
                plaintext = _cipher.Decrypt(templateId, envelope);
                return FaceTemplateCodec.Deserialize(plaintext, FaceModels.AlgorithmVersion, FaceModels.EmbeddingDimensions);
            }
            catch (FaceTemplateUnavailableException exception)
            {
                throw Unavailable(exception.Category);
            }
            finally
            {
                if (plaintext is not null)
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
        }

        private async Task<FaceAnalysis> AnalyzeAsync(FaceCapture capture, CancellationToken cancellationToken)
        {
            try
            {
                return await _engines.AnalyzeAsync(capture.Image, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw Unavailable("FaceEngineFailed", exception);
            }
        }

        // Strict "face:<guid>": anything else is not this provider's reference and is never interpreted.
        private Guid Parse(string subjectReference) =>
            FaceSubjectReference.TryParse(subjectReference, out var templateId) ? templateId : throw Unavailable("FaceReferenceInvalid");

        private ExternalServiceUnavailableException Unavailable(string category, Exception? exception = null)
        {
            // Category and exception type only: the exception message of a store or engine could carry a URL or a path.
            _logger.LogWarning("Face provider {Provider} unavailable: {Category} ({ExceptionType})",
                ProviderName, category, exception?.GetType().Name ?? "none");
            return new ExternalServiceUnavailableException("The face verification service is not available. Try again later.", exception);
        }

        private static void Clear(float[] values) => Array.Clear(values);
    }
}
