namespace SCI_PH.Application.Common.Security
{
    // Internal audit codes. Public responses stay generic to avoid account enumeration.
    public enum SecurityFailureReason
    {
        InvalidCredentials = 1,
        LockedOut = 2,
        AccountInactive = 3,
        TokenNotFound = 4,
        TokenExpired = 5,
        TokenRevoked = 6,
        TokenReused = 7,
        PasswordMismatch = 8,
        PasswordPolicy = 9,
        AccountNotFound = 10,
        InvalidToken = 11,
        InvitationExpired = 12,
        InvitationNotPending = 13,
        SubjectUnavailable = 14,
        EmailInUse = 15,
        InvalidMfaCode = 16,
        DeviceNotFound = 17,
        DeviceRevoked = 18,
        InvalidDeviceKey = 19,
        EmailDeliveryFailed = 20
    }
}
