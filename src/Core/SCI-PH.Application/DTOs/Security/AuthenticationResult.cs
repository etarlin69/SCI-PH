namespace SCI_PH.Application.DTOs.Security
{
    // RefreshToken is the raw value; it is returned once to the client and never stored or logged.
    public sealed record AuthenticationResult(
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAtUtc,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAtUtc,
        UserAccount User);
}
