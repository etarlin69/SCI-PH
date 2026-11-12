using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;

namespace SCI_PH.Mobile.Features.Auth.Services;

/// <summary>
/// Seguridad de la propia cuenta (personal clínico o paciente) con el cliente protegido común (Bearer y renovación ante 401).
/// Cambiar la contraseña o cerrar todas las sesiones revoca en el servidor TODOS los refresh tokens, también el de este
/// dispositivo: por eso, tras el éxito, la sesión local se cierra y hay que volver a iniciar sesión. Nunca guarda ni
/// registra contraseñas.
/// </summary>
public interface IAccountSecurityService
{
    Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    Task RevokeAllSessionsAsync(CancellationToken cancellationToken = default);
}

public sealed class AccountSecurityService : IAccountSecurityService
{
    private const string Auth = "api/mobile/auth";

    private readonly IMobileApiClient _client;
    private readonly IMobileAuthService _auth;

    public AccountSecurityService(IMobileApiClient client, IMobileAuthService auth)
    {
        _client = client;
        _auth = auth;
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        await _client.PostAsync($"{Auth}/change-password", new ChangePasswordBody(currentPassword, newPassword), cancellationToken);
        await EndLocalSessionAsync();
    }

    public async Task RevokeAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        await _client.PostAsync($"{Auth}/revoke-sessions", new { }, cancellationToken);
        await EndLocalSessionAsync();
    }

    // El refresh token ya está revocado: el logout solo borra la sesión local (ignora la respuesta del servidor).
    private async Task EndLocalSessionAsync()
    {
        try
        {
            await _auth.LogoutAsync();
        }
        catch (Exception)
        {
            // LogoutAsync borra la sesión local pase lo que pase.
        }
    }

    private sealed record ChangePasswordBody(string CurrentPassword, string NewPassword);
}
