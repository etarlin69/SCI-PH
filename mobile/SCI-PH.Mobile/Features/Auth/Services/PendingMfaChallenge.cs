using SCI_PH.Mobile.Common.Authentication.Contracts;

namespace SCI_PH.Mobile.Features.Auth.Services;

/// <summary>
/// Challenge MFA entre el login y la pantalla de verificación. Solo en memoria y solo durante ese paso: se borra al
/// completar, al volver al login o al caducar. Nunca se persiste.
/// </summary>
public sealed class PendingMfaChallenge
{
    private readonly object _gate = new();
    private MfaChallengeDto? _current;

    public MfaChallengeDto? Current
    {
        get { lock (_gate) { return _current; } }
    }

    public void Set(MfaChallengeDto challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        lock (_gate)
        {
            _current = challenge;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
        }
    }
}
