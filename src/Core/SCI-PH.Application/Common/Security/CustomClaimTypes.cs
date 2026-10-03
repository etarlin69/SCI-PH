namespace SCI_PH.Application.Common.Security
{
    // Short claim names used in access tokens. Standard JWT names (sub, jti, email) come from the JWT library.
    public static class CustomClaimTypes
    {
        public const string Permission = "permission";
        public const string Role = "role";
        public const string UserName = "name";
        // Distinguishes access tokens from short-lived MFA challenge tokens signed with the same key.
        public const string TokenUse = "token_use";
        public const string DeviceId = "device_id";
        public const string DeviceCode = "device_code";
        public const string DeviceScope = "device_scope";
    }
}
