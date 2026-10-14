namespace SCI_PH.Mobile.Common.Authentication.Contracts;

// Contratos de api/mobile/auth (docs/api/mobile-bff-contracts.md), propios de la app: no referencian el backend.
// Los que llevan contraseñas, códigos o tokens son clases y no records, para que ToString() no los imprima.

public sealed class LoginRequestDto
{
    public required string Identifier { get; init; }

    public required string Password { get; init; }
}

public sealed class MobileLoginResponseDto
{
    public bool MfaRequired { get; init; }

    public AuthenticationResultDto? Session { get; init; }

    public MfaChallengeDto? MfaChallenge { get; init; }
}

public sealed class MfaChallengeDto
{
    public required string Token { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }
}

/// <summary>Exactamente uno de <see cref="Code"/> (TOTP) o <see cref="RecoveryCode"/>.</summary>
public sealed class MfaVerifyRequestDto
{
    public required string ChallengeToken { get; init; }

    public string? Code { get; init; }

    public string? RecoveryCode { get; init; }
}

public sealed class AuthenticationResultDto
{
    public required string AccessToken { get; init; }

    public DateTimeOffset AccessTokenExpiresAtUtc { get; init; }

    public required string RefreshToken { get; init; }

    public DateTimeOffset RefreshTokenExpiresAtUtc { get; init; }

    public UserAccountDto? User { get; init; }
}

public sealed class RefreshTokenRequestDto
{
    public required string RefreshToken { get; init; }
}

/// <summary>Respuesta de <c>GET /me</c>.</summary>
public sealed record UserAccountDto(
    Guid Id,
    string UserName,
    string? Email,
    IReadOnlyList<string>? Roles,
    IReadOnlyList<string>? Permissions);
