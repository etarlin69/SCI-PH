namespace SCI_PH.Mobile.App.Models.Security;

// Solicitud de Login desde la pantalla de la App Móvil
public class LoginRequestDto
{
    public string Identifier { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

// Respuesta de Autenticación con Tokens JWT
public record AuthenticationResultDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    UserAccountDto User
);

// Manejo de Respuesta del Servidor (Soporta si el usuario tiene MFA/2FA activo)
public record LoginResultDto(
    bool MfaRequired,
    AuthenticationResultDto? Session,
    MfaChallengeDto? MfaChallenge
);

public record MfaChallengeDto(
    string Token,
    DateTimeOffset ExpiresAtUtc
);

public class MfaVerifyRequestDto
{
    public string ChallengeToken { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? RecoveryCode { get; set; }
}

public class RefreshTokenRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}