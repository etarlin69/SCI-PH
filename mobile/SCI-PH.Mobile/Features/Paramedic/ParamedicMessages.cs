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

    /// <summary>404: la emergencia ya no existe para este usuario (o no es una emergencia prehospitalaria).</summary>
    public const string EmergencyNotFound = "No se encontró la emergencia. Vuelve a abrirla desde el inicio.";

    public const string ProceduresCatalogFailed = "No se pudo cargar el catálogo de procedimientos.";

    /// <summary>409: la clave del procedimiento ya se usó con otros datos, o la emergencia ya no admite registros.</summary>
    public const string ProcedureConflict =
        "No se pudo registrar el procedimiento: la solicitud ya se usó con otros datos o la emergencia ya no admite registros.";

    public const string RegisterProcedureFailed = "No se pudo guardar el procedimiento. Intenta nuevamente.";

    public const string MedicationsCatalogFailed = "No se pudo cargar el catálogo de medicamentos.";

    /// <summary>404: la emergencia o el medicamento ya no existen para este usuario.</summary>
    public const string MedicationOrEmergencyNotFound =
        "No se encontró la emergencia o el medicamento. Vuelve a cargar el catálogo o abre la emergencia desde el inicio.";

    /// <summary>409: el medicamento ya no está activo, la clave se usó con otros datos o la emergencia no admite registros.</summary>
    public const string MedicationAdministrationConflict =
        "No se pudo registrar la dosis: el medicamento ya no está activo, la solicitud ya se usó con otros datos o la emergencia ya no admite registros.";

    public const string RegisterMedicationFailed = "No se pudo guardar el medicamento. Intenta nuevamente.";

    public const string MedicationRecorded = "Dosis registrada. Puedes registrar otra.";

    /// <summary>409: la clave de la observación ya se usó con otro texto u hora, o la emergencia ya no admite registros.</summary>
    public const string NoteConflict =
        "No se pudo registrar la observación: la solicitud ya se usó con otros datos o la emergencia ya no admite registros.";

    public const string RegisterNoteFailed = "No se pudo guardar la observación. Intenta nuevamente.";

    public const string NoteRecorded = "Observación registrada. Puedes agregar otra.";

    /// <summary>Hora de la observación antes del primer intento de guardar: se fija automáticamente.</summary>
    public const string NoteTimePending = "Se registrará con la hora al guardar.";
}
