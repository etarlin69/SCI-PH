namespace SCI_PH.Mobile.Features.Paramedic;

/// <summary>Textos del área paramédica. Los de red, permisos y servidor son los comunes de MobileApiErrorMessages.</summary>
public static class ParamedicMessages
{
    public const string CreateEmergencyFailed = "No se pudo crear la emergencia. Intenta nuevamente.";

    /// <summary>409: el clientRequestId ya se usó con otros datos (por ejemplo, el motivo cambió tras un fallo de red).</summary>
    public const string EmergencyRequestReused =
        "La solicitud ya fue utilizada con datos diferentes. Vuelve a iniciar la creación de la emergencia.";
}
