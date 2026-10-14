namespace SCI_PH.Mobile.Common.Authentication;

/// <summary>
/// Almacenamiento persistente y cifrado del refresh token del personal clínico: lo único que sobrevive a cerrar la
/// app. Nunca guarda el access token, la contraseña ni códigos MFA.
/// </summary>
public interface ISecureTokenStore
{
    Task<string?> GetRefreshTokenAsync();

    /// <summary>Guarda o reemplaza el refresh token (rotación).</summary>
    Task SaveRefreshTokenAsync(string refreshToken);

    Task ClearAsync();
}

/// <summary>
/// Sobre <see cref="ISecureStorage"/> de MAUI (<c>SecureStorage.Default</c>): Keystore en Android, DPAPI en Windows,
/// Keychain en iOS/Mac.
/// </summary>
public sealed class MauiSecureTokenStore : ISecureTokenStore
{
    internal const string RefreshTokenKey = "sciph.clinical.refresh-token";

    private readonly ISecureStorage _storage;

    public MauiSecureTokenStore(ISecureStorage storage)
    {
        _storage = storage;
    }

    public async Task<string?> GetRefreshTokenAsync()
    {
        try
        {
            var token = await _storage.GetAsync(RefreshTokenKey);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (Exception)
        {
            // Valor ilegible (por ejemplo, restaurado de una copia de seguridad sin la clave del dispositivo):
            // se descarta y el usuario vuelve a iniciar sesión.
            _storage.Remove(RefreshTokenKey);
            return null;
        }
    }

    public Task SaveRefreshTokenAsync(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        return _storage.SetAsync(RefreshTokenKey, refreshToken);
    }

    public Task ClearAsync()
    {
        _storage.Remove(RefreshTokenKey);
        return Task.CompletedTask;
    }
}
