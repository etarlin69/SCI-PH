namespace SCI_PH.Application.Common.Security
{
    // Device API scopes. Devices report and resolve identification events and store enrollments on their own sensor; they
    // never get clinical permissions. Enroll is separate from Identify (a reader may hold both).
    public static class DeviceScopes
    {
        public const string Heartbeat = "Device.Heartbeat";
        public const string FingerprintIdentify = "Device.Fingerprint.Identify";
        public const string FingerprintEnroll = "Device.Fingerprint.Enroll";

        public static IReadOnlyList<string> All { get; } = [Heartbeat, FingerprintIdentify, FingerprintEnroll];
    }
}
