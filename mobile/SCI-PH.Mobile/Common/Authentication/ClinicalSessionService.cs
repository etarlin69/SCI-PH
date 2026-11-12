namespace SCI_PH.Mobile.Common.Authentication;

/// <summary>
/// Sesión de la cuenta autenticada (personal clínico o paciente), solo en memoria: access token, su caducidad y el
/// usuario de <c>/me</c>. El refresh token no vive aquí sino en <see cref="ISecureTokenStore"/>. La app tiene una sola
/// sesión; su <see cref="Area"/> decide si se muestra el área paramédica o la del paciente.
/// </summary>
public interface IClinicalSessionService
{
    string? AccessToken { get; }

    DateTimeOffset? AccessTokenExpiresAtUtc { get; }

    ClinicalUser? CurrentUser { get; }

    /// <summary>Hay access token y un usuario verificado con <c>/me</c>.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Sesión autenticada con permiso para el área paramédica.</summary>
    bool CanOperateEmergencies { get; }

    /// <summary>Área de la sesión autenticada; <see cref="AccountArea.None"/> sin sesión.</summary>
    AccountArea Area { get; }

    /// <summary>Se dispara al iniciar, renovar o borrar la sesión.</summary>
    event EventHandler? SessionChanged;

    void SetAuthentication(string accessToken, DateTimeOffset accessTokenExpiresAtUtc);

    void SetCurrentUser(ClinicalUser user);

    void Clear();
}

public sealed class ClinicalSessionService : IClinicalSessionService
{
    private readonly object _gate = new();
    private string? _accessToken;
    private DateTimeOffset? _accessTokenExpiresAtUtc;
    private ClinicalUser? _currentUser;

    public string? AccessToken
    {
        get { lock (_gate) { return _accessToken; } }
    }

    public DateTimeOffset? AccessTokenExpiresAtUtc
    {
        get { lock (_gate) { return _accessTokenExpiresAtUtc; } }
    }

    public ClinicalUser? CurrentUser
    {
        get { lock (_gate) { return _currentUser; } }
    }

    public bool IsAuthenticated
    {
        get { lock (_gate) { return _accessToken is not null && _currentUser is not null; } }
    }

    public bool CanOperateEmergencies
    {
        get { lock (_gate) { return _accessToken is not null && _currentUser?.CanOperateEmergencies == true; } }
    }

    public AccountArea Area
    {
        get { lock (_gate) { return _accessToken is not null && _currentUser is not null ? _currentUser.Area : AccountArea.None; } }
    }

    public event EventHandler? SessionChanged;

    public void SetAuthentication(string accessToken, DateTimeOffset accessTokenExpiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        lock (_gate)
        {
            _accessToken = accessToken;
            _accessTokenExpiresAtUtc = accessTokenExpiresAtUtc;
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCurrentUser(ClinicalUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        lock (_gate)
        {
            _currentUser = user;
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _accessToken = null;
            _accessTokenExpiresAtUtc = null;
            _currentUser = null;
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}
