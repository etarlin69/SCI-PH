using SCI_PH.Mobile.Common.Authentication;

namespace SCI_PH.Mobile.Tests.Patient;

internal static class PatientSession
{
    public static ClinicalSessionService SignedIn(Guid? userId = null)
    {
        var session = new ClinicalSessionService();
        SignIn(session, userId ?? Guid.NewGuid());
        return session;
    }

    public static void SignIn(IClinicalSessionService session, Guid userId)
    {
        session.SetAuthentication($"access-{userId:N}", DateTimeOffset.UtcNow.AddMinutes(15));
        session.SetCurrentUser(new ClinicalUser(userId, "paciente", null, [AccountRoles.Patient], []));
    }
}
