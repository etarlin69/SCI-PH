namespace SCI_PH.Mobile.Features.Paramedic;

/// <summary>Textos del área paramédica. Los de red, permisos y servidor son los comunes de MobileApiErrorMessages.</summary>
public static class ParamedicMessages
{
    public const string CreateEmergencyFailed = "No se pudo crear la emergencia. Intenta nuevamente.";

    public const string ActiveEmergenciesFailed = "No se pudieron cargar las emergencias activas. Intenta nuevamente.";

    public const string OpenEmergencyFailed = "No se pudo abrir la emergencia. Intenta nuevamente.";

    /// <summary>Al retomarla, el servidor ya no la deja continuar (entregada al hospital, cerrada o sin acceso).</summary>
    public const string EmergencyNoLongerActive = "Esa emergencia ya no está activa. La lista se actualizó.";

    /// <summary>409: el clientRequestId ya se usó con otros datos (por ejemplo, el motivo cambió tras un fallo de red).</summary>
    public const string EmergencyRequestReused =
        "La solicitud ya fue utilizada con datos diferentes. Vuelve a iniciar la creación de la emergencia.";

    /// <summary>404: ningún paciente tiene ese documento. La emergencia sigue NN y puede continuar sin identificar.</summary>
    public const string PatientNotFoundByDocument = "No se encontró un paciente con ese documento.";

    /// <summary>409: ya identificada por otra solicitud, o la solicitud se usó con otro documento.</summary>
    public const string IdentificationConflict =
        "No se pudo identificar con este documento: la emergencia ya fue identificada o la solicitud se usó con otros datos.";

    /// <summary>La búsqueda del documento no respondió (sin conexión o error del servidor): nada cambió.</summary>
    public const string DocumentResolveFailed = "No se pudo buscar el paciente en este momento. Intenta nuevamente.";

    /// <summary>La confirmación no respondió: pudo aplicarse; el reintento usa la misma solicitud y nunca identifica dos veces.</summary>
    public const string DocumentConfirmFailed = "No se pudo confirmar al paciente. Intenta nuevamente: no se identificará dos veces.";

    /// <summary>404 al confirmar: el documento ya no corresponde a un paciente. La emergencia sigue NN.</summary>
    public const string DocumentCandidateGone = "El documento ya no corresponde a un paciente. Búscalo de nuevo.";

    /// <summary>409 y la emergencia ya tiene paciente (otra vía la identificó): se muestra ese paciente, nunca se sustituye.</summary>
    public const string AlreadyIdentifiedByAnother = "La emergencia ya fue identificada. Se muestra el paciente registrado.";

    /// <summary>El candidato no tiene fecha de nacimiento registrada.</summary>
    public const string CandidateBirthDateUnknown = "No registrada";

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

    public const string TransferDestinationsFailed = "No se pudo cargar la lista de destinos.";

    public const string RequestTransferFailed = "No se pudo solicitar el traslado. Intenta nuevamente.";

    public const string TransferStepFailed = "No se pudo actualizar el traslado. Intenta nuevamente.";

    /// <summary>404: la emergencia, el traslado o el destino ya no existen para este usuario.</summary>
    public const string TransferNotFound = "No se encontró la emergencia, el traslado o el destino. Vuelve a cargar la pantalla.";

    /// <summary>409: destino inactivo, ya hay otro traslado, estado incompatible, la solicitud se usó con otros datos o el hospital ya recibió al paciente.</summary>
    public const string TransferConflict =
        "No se pudo completar: el destino no está activo, ya hay otro traslado, el traslado cambió de estado o el hospital ya recibió al paciente.";

    public const string TransferCancelled = "Traslado cancelado. Puedes solicitar otro destino.";

    /// <summary>La emergencia ya la recibió el hospital: el traslado queda solo de lectura.</summary>
    public const string TransferHandedOff = "El caso ya fue recibido por el hospital. La atención prehospitalaria ha finalizado.";

    public const string FingerprintReadersFailed = "No se pudo cargar la lista de lectores de huella.";

    public const string NoFingerprintReaders = "No hay lectores de huella disponibles. Identifica con documento o continúa sin identificar.";

    public const string FingerprintReaderRequired = "Selecciona el lector de huella.";

    public const string FingerprintWaiting = "Pide al paciente colocar el dedo en el lector.";

    public const string FingerprintNoMatch = "No se encontró una coincidencia.";

    public const string FingerprintLowQuality = "Lectura de huella insuficiente. Intente nuevamente.";

    public const string FingerprintSensorError = "Lector no disponible.";

    public const string FingerprintExpired = "La lectura venció. Intente nuevamente.";

    public const string FingerprintCancelled = "Lectura de huella cancelada.";

    /// <summary>404 al abrir: el lector ya no está activo o la emergencia no existe para este usuario.</summary>
    public const string FingerprintReaderNotFound = "El lector ya no está disponible o no se encontró la emergencia. Vuelve a cargar los lectores.";

    /// <summary>409 al abrir: ya hay otra lectura en curso, el lector está ocupado, la emergencia ya fue identificada o la recibió el hospital.</summary>
    public const string FingerprintOpenConflict =
        "No se pudo iniciar la lectura: ya hay otra en curso, el lector está ocupado o la emergencia ya fue identificada o recibida por el hospital.";

    /// <summary>409 al confirmar: la sesión venció o cambió, la referencia se revocó o la emergencia ya fue identificada o recibida.</summary>
    public const string FingerprintConfirmConflict =
        "No se pudo confirmar: la lectura venció o cambió, o la emergencia ya fue identificada o recibida por el hospital.";

    public const string FingerprintFailed = "No se pudo completar la identificación por huella. Intenta nuevamente.";

    /// <summary>Sin conexión mientras se consulta la lectura: se sigue intentando hasta que venza.</summary>
    public const string FingerprintCheckingOffline = "Sin conexión. Se sigue consultando la lectura…";

    /// <summary>El código leído no es de SCI-PH (no se envía al servidor).</summary>
    public const string QrInvalidCode = "Código no válido.";

    /// <summary>404: cualquier QR no válido (vencido, sustituido, revocado, desconocido). Nunca se distingue la causa.</summary>
    public const string QrInvalidOrExpired = "El código no es válido o ya expiró. Pide al paciente actualizar su QR.";

    /// <summary>409 y la emergencia, leída de nuevo, ya tiene paciente: nunca se sustituye.</summary>
    public const string QrAlreadyIdentified = "La emergencia ya está identificada.";

    /// <summary>409 y la emergencia sigue sin identificar (por ejemplo, el hospital ya la recibió).</summary>
    public const string QrConflict = "No se pudo identificar con este código: la emergencia cambió. Vuelve a la identificación.";

    public const string QrResolveFailed = "No se pudo leer el código en este momento. Intenta nuevamente.";

    /// <summary>Sin respuesta o error temporal al confirmar: el reintento usa la misma solicitud y nunca identifica dos veces.</summary>
    public const string QrConfirmFailed = "No se pudo confirmar al paciente. Intenta nuevamente: no se identificará dos veces.";

    public const string CameraUnavailable = "Cámara no disponible.";

    public const string QrScannerUnsupported = "La lectura de QR no está disponible en este dispositivo.";

    public const string CameraPermissionDenied = "Se necesita permiso de cámara para escanear el QR del paciente.";

    public const string CameraPermissionDeniedPermanently =
        "El permiso de cámara está desactivado. Actívalo en la configuración de la app para escanear el QR.";

    /// <summary>Hora de la observación antes del primer intento de guardar: se fija automáticamente.</summary>
    public const string NoteTimePending = "Se registrará con la hora al guardar.";
}
