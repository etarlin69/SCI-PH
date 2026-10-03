namespace SCI_PH.Application.Common.Security
{
    // Device API scopes. Devices report and resolve identification events; they never get clinical permissions.
    public static class DeviceScopes
    {
        public const string Heartbeat = "Device.Heartbeat";
        public const string FingerprintIdentify = "Device.Fingerprint.Identify";

        public static IReadOnlyList<string> All { get; } = [Heartbeat, FingerprintIdentify];
    }
}
