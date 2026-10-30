namespace SCI_PH.Infrastructure.Biometrics.Face.OpenCv
{
    // FaceVerification:OpenCv (ADR-012, PR 2). Everything is server-side: no request, form, query or header selects a model,
    // a threshold or a key. Keys and the Blob connection string come from User Secrets, environment variables or a secret
    // store, never from committed appsettings, the database, Blob or the image.
    public sealed class OpenCvFaceOptions
    {
        public const string SectionName = "FaceVerification:OpenCv";

        // Relative paths are resolved against the host's content root (the image copies the models to models/face/).
        public string YuNetModelPath { get; set; } = $"models/face/{FaceModels.YuNetFileName}";
        public string SFaceModelPath { get; set; } = $"models/face/{FaceModels.SFaceFileName}";

        // Cosine similarity of the L2-normalized SFace embeddings (OpenCV samples/dnn/face_detect.py: 0.363). Initial,
        // upstream-derived value: NOT calibrated for SCI-PH webcam captures.
        public double CosineThreshold { get; set; } = 0.363;

        // YuNet: faces under DetectorScore are ignored; a best face under GoodScore is LowQuality, two or more over it are
        // MultipleFaces. A face whose shorter side is under MinFaceSide px (normalized capture) is LowQuality.
        public float DetectorScore { get; set; } = 0.6f;
        public float GoodScore { get; set; } = 0.9f;
        public int MinFaceSide { get; set; } = 80;

        // Detector/recognizer pairs that may run at once (they are not thread-safe). 0: max(1, processors / 2), at most 4.
        public int EngineCapacity { get; set; }

        public TemplateStoreOptions Templates { get; set; } = new();
        public EncryptionOptions Encryption { get; set; } = new();
        public CleanupOptions Cleanup { get; set; } = new();

        public int ResolveEngineCapacity() =>
            EngineCapacity > 0 ? EngineCapacity : Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

        public sealed class TemplateStoreOptions
        {
            // Azure Blob / Azurite connection string (a secret).
            public string? ConnectionString { get; set; }

            // Private container dedicated to this provider's templates.
            public string Container { get; set; } = "face-templates";
        }

        public sealed class EncryptionOptions
        {
            // Key used for new templates; every configured key can still read the templates written with it.
            public string? ActiveKeyId { get; set; }

            // keyId -> base64 of exactly 32 bytes (AES-256).
            public Dictionary<string, string> Keys { get; set; } = [];
        }

        public sealed class CleanupOptions
        {
            public int IntervalSeconds { get; set; } = 30;
            public int BatchSize { get; set; } = 50;

            // Report-only comparison of the store with the clinical references (FaceTemplateReconciler).
            public int ReconcileIntervalMinutes { get; set; } = 360;
        }
    }
}
