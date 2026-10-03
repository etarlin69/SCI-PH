namespace SCI_PH.Application.DTOs.Security
{
    // Request metadata for the security audit. Built by the presentation layer from the HTTP request.
    public sealed record ClientContext(string? IpAddress, string? UserAgent)
    {
        public static ClientContext Unknown { get; } = new(null, null);
    }
}
