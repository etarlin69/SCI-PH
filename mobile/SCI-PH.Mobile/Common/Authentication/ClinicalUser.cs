namespace SCI_PH.Mobile.Common.Authentication;

/// <summary>Permisos del backend que usa la app (nombres de <c>Permissions</c> del servidor).</summary>
public static class ClinicalPermissions
{
    /// <summary>Operar el flujo prehospitalario: entrar al área paramédica.</summary>
    public const string EmergenciesOperate = "Emergencies.Operate";
}

/// <summary>Roles del backend que la app necesita conocer (nombres de <c>SecurityRoles</c> del servidor).</summary>
public static class AccountRoles
{
    /// <summary>Cuenta de paciente: no tiene permisos; el servidor le da solo su propio expediente (ADR-015).</summary>
    public const string Patient = "Patient";
}

/// <summary>Área de la app a la que entra una cuenta autenticada.</summary>
public enum AccountArea
{
    /// <summary>La cuenta no tiene acceso a la app: la sesión se descarta.</summary>
    None,

    /// <summary>Personal con <see cref="ClinicalPermissions.EmergenciesOperate"/>: área paramédica.</summary>
    Paramedic,

    /// <summary>Cuenta con el rol <see cref="AccountRoles.Patient"/>: área del paciente.</summary>
    Patient
}

/// <summary>
/// Datos mínimos de la cuenta autenticada según <c>GET /me</c>, del personal clínico o de un paciente: un solo inicio de
/// sesión para ambos (ADR-015). El área del personal se decide por permiso (<c>Emergencies.Operate</c>), nunca por rol;
/// la del paciente por el rol <c>Patient</c>, porque ese rol no tiene permisos. El servidor vuelve a decidir cada acceso.
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

    public bool IsPatient => Roles.Contains(AccountRoles.Patient, StringComparer.Ordinal);

    /// <summary>
    /// Paciente sin permisos de personal → área del paciente; personal con el permiso → área paramédica. Una cuenta que
    /// fuera ambas cosas (el servidor no lo permite) o ninguna no entra.
    /// </summary>
    public AccountArea Area => (IsPatient, CanOperateEmergencies) switch
    {
        (true, false) => AccountArea.Patient,
        (false, true) => AccountArea.Paramedic,
        _ => AccountArea.None
    };
}
