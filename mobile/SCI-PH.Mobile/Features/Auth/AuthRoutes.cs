using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Patient;

namespace SCI_PH.Mobile.Features.Auth;

/// <summary>
/// Rutas del inicio de sesión (personal clínico y pacientes, ADR-015); se registran con
/// <see cref="AuthServiceCollectionExtensions.RegisterAuthRoutes"/>.
/// </summary>
public static class AuthRoutes
{
    /// <summary>Se apila sobre la entrada compartida ("Volver" regresa a la entrada).</summary>
    public const string Login = "ClinicalLogin";

    /// <summary>Segundo factor; se apila sobre el login.</summary>
    public const string Mfa = "ClinicalMfa";

    /// <summary>
    /// Área paramédica tras autenticar: reemplaza la pila (login y MFA dejan de estar detrás) y deja la entrada como raíz.
    /// </summary>
    public static string ClinicalArea { get; } = $"{AppRoutes.Root(AppRoutes.Entry)}/{ParamedicRoutes.NewEmergency}";

    /// <summary>Área del paciente tras autenticar: su inicio, como raíz (el menú lateral lleva al resto).</summary>
    public static string PatientArea { get; } = AppRoutes.Root(PatientRoutes.Home);

    /// <summary>Destino de una sesión verificada según su área; sin área, la entrada.</summary>
    public static string AreaRoute(AccountArea area) => area switch
    {
        AccountArea.Paramedic => ClinicalArea,
        AccountArea.Patient => PatientArea,
        _ => AppRoutes.Root(AppRoutes.Entry)
    };
}
