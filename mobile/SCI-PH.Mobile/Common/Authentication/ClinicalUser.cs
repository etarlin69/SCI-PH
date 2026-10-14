namespace SCI_PH.Mobile.Common.Authentication;

/// <summary>Permisos del backend que usa la app (nombres de <c>Permissions</c> del servidor).</summary>
public static class ClinicalPermissions
{
    /// <summary>Operar el flujo prehospitalario: entrar al área paramédica.</summary>
    public const string EmergenciesOperate = "Emergencies.Operate";
}

/// <summary>
/// Datos mínimos de la cuenta del personal clínico según <c>GET /me</c>. El acceso se decide por permisos, nunca por
/// rol: los roles se conservan solo para mostrarlos.
/// </summary>
public sealed record ClinicalUser(
    Guid Id,
    string UserName,
    string? Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions)
{
    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);

    public bool CanOperateEmergencies => HasPermission(ClinicalPermissions.EmergenciesOperate);
}
