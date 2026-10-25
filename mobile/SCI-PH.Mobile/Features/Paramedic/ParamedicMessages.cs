namespace SCI_PH.Mobile.Features.Paramedic;

/// <summary>Textos del área paramédica. Los de red, permisos y servidor son los comunes de MobileApiErrorMessages.</summary>
public static class ParamedicMessages
{
    public const string CreateEmergencyFailed = "No se pudo crear la emergencia. Intenta nuevamente.";

    /// <summary>409: el clientRequestId ya se usó con otros datos (por ejemplo, el motivo cambió tras un fallo de red).</summary>
    public const string EmergencyRequestReused =
        "La solicitud ya fue utilizada con datos diferentes. Vuelve a iniciar la creación de la emergencia.";

    /// <summary>404: ningún paciente tiene ese documento. La emergencia sigue NN y puede continuar sin identificar.</summary>
    public const string PatientNotFoundByDocument = "No se encontró un paciente con ese documento.";

    /// <summary>409: ya identificada por otra solicitud, o la solicitud se usó con otro documento.</summary>
    public const string IdentificationConflict =
        "No se pudo identificar con este documento: la emergencia ya fue identificada o la solicitud se usó con otros datos.";

    public const string IdentifyByDocumentFailed = "No se pudo identificar con el documento. Intenta nuevamente.";

    public const string DocumentTypeRequired = "Selecciona el tipo de documento.";

    /// <summary>409: la clave de la toma ya se usó con otros valores, o la emergencia ya no admite registros.</summary>
    public const string VitalSignsConflict =
        "No se pudo registrar la toma: la solicitud ya se usó con otros datos o la emergencia ya no admite registros.";

    public const string RegisterVitalSignsFailed = "No se pudieron guardar los signos vitales. Intenta nuevamente.";
}
