namespace SCI_PH.Infrastructure.Biometrics.Face.OpenCv
{
    // The exact models of the local face provider (ADR-012, PR 2), pinned by SHA-256. They are never committed: the script
    // scripts/biometrics/fetch-face-models downloads them from the pinned upstream URLs and checks these hashes, and the host
    // refuses to start with a different file.
    public static class FaceModels
    {
        // OpenCV Zoo face detector YuNet, 2023mar (MIT). Fixed input shape: the input size is set per image.
        public const string YuNetFileName = "face_detection_yunet_2023mar.onnx";
        public const string YuNetSha256 = "8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4";
        public const string YuNetUrl = "https://huggingface.co/opencv/face_detection_yunet/resolve/main/face_detection_yunet_2023mar.onnx";

        // OpenCV Zoo face recognizer SFace, 2021dec, FP32 (Apache-2.0). 128-dimensional embeddings.
        public const string SFaceFileName = "face_recognition_sface_2021dec.onnx";
        public const string SFaceSha256 = "0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79";
        public const string SFaceUrl = "https://huggingface.co/opencv/face_recognition_sface/resolve/main/face_recognition_sface_2021dec.onnx";

        public const int EmbeddingDimensions = 128;

        // Stored as PatientBiometricReference.AlgorithmVersion and inside every template: detector + recognizer versions.
        public const string AlgorithmVersion = "opencv-sface-2021dec-yunet-2023mar";
    }
}
