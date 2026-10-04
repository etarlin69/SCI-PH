namespace SCI_PH.Application.Common.Security
{
    public enum RefreshTokenRevocationReason
    {
        Rotated = 1,
        Logout = 2,
        ReuseDetected = 3,
        SessionsRevoked = 4,
        PasswordChanged = 5,
        AccountDeactivated = 6,
        RolesChanged = 7,
        MfaChanged = 8,
        PasswordReset = 9
    }
}
