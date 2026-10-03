namespace SCI_PH.Persistence.Identity.Models
{
    internal static class SecurityFieldLengths
    {
        // Longest textual IPv6 address.
        public const int IpAddress = 45;
        public const int UserAgent = 256;
        // Lowercase hexadecimal SHA-256.
        public const int TokenHash = 64;
        public const int Email = 256;
        public const int RoleName = 64;
        public const int DeviceCode = 60;
        public const int DeviceName = 120;
        public const int DeviceScopes = 256;
        public const int FirmwareVersion = 32;

        public static string? Limit(string? value, int maxLength) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
    }
}
