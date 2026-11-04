using System.Net;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.ViewModels;

namespace SCI_PH.Mobile.Features.Patient;

/// <summary>
/// Textos de error del área del paciente. Nunca muestran ProblemDetails, el estado HTTP ni la excepción, y nunca hay
/// datos simulados de respaldo: si algo falla, la pantalla queda vacía con el mensaje.
/// </summary>
public static class PatientMessages
{
    /// <summary>401 aun tras renovar la sesión: <c>AuthenticatedHttpHandler</c> ya la cerró y la app vuelve a la entrada.</summary>
    public const string SessionEnded = "Tu sesión terminó. Inicia sesión de nuevo.";

    /// <summary>403: la cuenta no es de paciente o no está vinculada a un expediente (el servidor falla cerrado).</summary>
    public const string NoRecordAccess =
        "Tu cuenta no tiene acceso a un expediente clínico en SCI-PH. Contacta a tu centro de salud.";

    /// <summary>404: el recurso pedido no existe o no es del paciente.</summary>
    public const string NotAvailable = "La información solicitada no está disponible.";

    public static string For(Exception exception) => exception switch
    {
        MobileApiException { StatusCode: HttpStatusCode.NotFound } => NotAvailable,
        MobileApiException { Error: MobileApiError.Unauthorized } => SessionEnded,
        MobileApiException { Error: MobileApiError.Forbidden } => NoRecordAccess,
        MobileApiException api => MobileApiErrorMessages.For(api),
        _ => BaseViewModel.GenericErrorMessage
    };
}
