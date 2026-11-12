using OpenCvSharp;
using SCI_PH.Application.Abstractions.Biometrics;

namespace SCI_PH.Infrastructure.Biometrics.Face.OpenCv
{
    // The result of analysing one capture: a rejection outcome, or the L2-normalized embedding of its single good face.
    internal readonly record struct FaceAnalysis(FaceVerificationOutcome? Rejected, float[]? Embedding);

    // One YuNet detector + SFace recognizer pair. Not thread-safe (SetInputSize mutates the detector): FaceEnginePool lends
    // each pair to one caller at a time. Every OpenCV object is disposed; nothing of the capture is kept.
    internal sealed class FaceEngine : IDisposable
    {
        private const int ScoreColumn = 14;

        private readonly FaceDetectorYN _detector;
        private readonly FaceRecognizerSF _recognizer;
        private readonly OpenCvFaceOptions _options;

        public FaceEngine(string yuNetPath, string sFacePath, OpenCvFaceOptions options)
        {
            _options = options;
            // Input size is set per image; NMS 0.3 and top-k 5000 are YuNet's upstream defaults.
            _detector = FaceDetectorYN.Create(yuNetPath, "", new Size(320, 320), options.DetectorScore, 0.3f, 5000);
            try
            {
                _recognizer = FaceRecognizerSF.Create(sFacePath, "");
            }
            catch
            {
                _detector.Dispose();
                throw;
            }
        }

        public FaceAnalysis Analyze(byte[] image)
        {
            using var mat = Cv2.ImDecode(image, ImreadModes.Color);
            if (mat.Empty())
            {
                // A normalized capture always decodes; anything else is a provider failure, not a patient outcome.
                throw new InvalidOperationException("A normalized face capture could not be decoded.");
            }

            _detector.SetInputSize(mat.Size());
            using var faces = new Mat();
            _detector.Detect(mat, faces);
            var detected = new DetectedFace[faces.Rows];
            for (var i = 0; i < faces.Rows; i++)
            {
                detected[i] = new DetectedFace(
                    faces.At<float>(i, 0), faces.At<float>(i, 1), faces.At<float>(i, 2), faces.At<float>(i, 3), faces.At<float>(i, ScoreColumn));
            }

            if (FaceQuality.Assess(detected, mat.Width, mat.Height, _options, out var best) is { } rejected)
            {
                return new FaceAnalysis(rejected, null);
            }

            using var row = faces.Row(best);
            using var aligned = new Mat();
            _recognizer.AlignCrop(mat, row, aligned);
            using var feature = new Mat();
            _recognizer.Feature(aligned, feature);
            if (feature.Total() != FaceModels.EmbeddingDimensions)
            {
                throw new InvalidOperationException("The face recognizer returned an unexpected embedding size.");
            }

            feature.GetArray(out float[] embedding);
            FaceQuality.Normalize(embedding);
            return new FaceAnalysis(null, embedding);
        }

        public void Dispose()
        {
            _recognizer.Dispose();
            _detector.Dispose();
        }
    }

    // Detection + quality + embedding of one normalized capture (the pool below; a stub in tests).
    internal interface IFaceAnalyzer
    {
        Task<FaceAnalysis> AnalyzeAsync(byte[] image, CancellationToken cancellationToken);
    }

    // A bounded pool of engines: at most `capacity` analyses run at once, each on its own pair; the others wait. Pairs are
    // created on first need (the first one at startup, so a broken native runtime or model fails the host) and disposed with
    // the pool at shutdown.
    internal sealed class FaceEnginePool : IFaceAnalyzer, IDisposable
    {
        private readonly Func<FaceEngine> _create;
        private readonly SemaphoreSlim _slots;
        private readonly System.Collections.Concurrent.ConcurrentBag<FaceEngine> _idle = [];
        private readonly List<FaceEngine> _all = [];
        private bool _disposed;

        public FaceEnginePool(Func<FaceEngine> create, int capacity)
        {
            _create = create;
            _slots = new SemaphoreSlim(capacity, capacity);
            Return(Track(create()));
        }

        public int Created
        {
            get
            {
                lock (_all)
                {
                    return _all.Count;
                }
            }
        }

        public async Task<FaceAnalysis> AnalyzeAsync(byte[] image, CancellationToken cancellationToken)
        {
            await _slots.WaitAsync(cancellationToken);
            FaceEngine? engine = null;
            try
            {
                engine = _idle.TryTake(out var idle) ? idle : Track(_create());
                return engine.Analyze(image);
            }
            finally
            {
                if (engine is not null)
                {
                    Return(engine);
                }

                _slots.Release();
            }
        }

        public void Dispose()
        {
            lock (_all)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                foreach (var engine in _all)
                {
                    engine.Dispose();
                }

                _all.Clear();
            }

            _slots.Dispose();
        }

        private FaceEngine Track(FaceEngine engine)
        {
            lock (_all)
            {
                _all.Add(engine);
            }

            return engine;
        }

        private void Return(FaceEngine engine) => _idle.Add(engine);
    }
}
