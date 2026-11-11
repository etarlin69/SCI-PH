// Tipos del frontend hospitalario alineados con los contratos clínicos del Web BFF (src/Core/SCI-PH.Application/DTOs/Clinical).
// Los enums viajan como TEXTO con el nombre del miembro C# ("Emergency", "InProgress"); el BFF rechaza números.

export type Guid = string;
/** Instante UTC en ISO-8601 (DateTimeOffset en el backend). */
export type UtcDateTime = string;
/** Fecha sin hora `yyyy-mm-dd` (DateOnly en el backend). */
export type DateOnly = string;

// ---------------------------------------------------------------- Patients

export type BiologicalSex = 'Female' | 'Male' | 'Intersex' | 'Unknown';
export type PatientIdentityStatus = 'Unverified' | 'Verified';
export type PatientIdentifierType = 'NationalId' | 'Passport' | 'QrCode' | 'InternalCode' | 'Phone' | 'Email' | 'Other';

export interface PatientIdentifierSummary {
    type: PatientIdentifierType;
    /** Valor tal como lo devuelva el backend (posiblemente enmascarado). */
    value: string;
}

/** Resumen de Patient para búsquedas y cabeceras. La edad se deriva de birthDate en la UI. */
export interface PatientSummary {
    id: Guid;
    displayName: string;
    birthDate: DateOnly | null;
    biologicalSex: BiologicalSex;
    identityStatus: PatientIdentityStatus;
    primaryIdentifier: PatientIdentifierSummary | null;
}

/** Búsqueda por identificador o nombre. Se envía en el cuerpo (POST) para no exponer la cédula en la URL. */
export interface PatientSearchRequest {
    identifierType?: PatientIdentifierType;
    value: string;
}

/** Patient.Register + identificador inicial opcional. El backend normaliza el valor; el cliente no. */
export interface RegisterPatientRequest {
    givenNames: string;
    familyNames: string;
    dateOfBirth: DateOnly | null;
    sex: BiologicalSex;
    identifier: { type: PatientIdentifierType; value: string; issuer?: string | null } | null;
}

// ---------------------------------------------------------------- Contexto clínico

export interface ClinicSummary {
    id: Guid;
    name: string;
}

/** Clínicas donde el usuario actúa como ClinicalStaff (afiliaciones activas). */
export interface ClinicalContext {
    staffId: Guid;
    clinics: ClinicSummary[];
}

// ---------------------------------------------------------------- Encounters

export type ClinicalEncounterStatus = 'Created' | 'InProgress' | 'Transferred' | 'Received' | 'InEvolution' | 'Closed' | 'Cancelled';
export type ClinicalEncounterType = 'Consultation' | 'Emergency' | 'Prehospital' | 'Hospitalization';

/**
 * ClinicalEncounter. El paciente es opcional: un encuentro NN no tiene patientId y se identifica por
 * temporaryPatientCode (`NN-` + 32 hex, generado por el backend). Tras identificarlo, el código se conserva.
 */
export interface ClinicalEncounterSummary {
    id: Guid;
    patientId: Guid | null;
    patient: PatientSummary | null;
    temporaryPatientCode: string | null;
    clinicId: Guid | null;
    type: ClinicalEncounterType;
    status: ClinicalEncounterStatus;
    reason: string | null;
    startedAtUtc: UtcDateTime;
    endedAtUtc: UtcDateTime | null;
    /** Momento de la identificación de un NN; null si nació identificado o sigue sin identificar. */
    identifiedAtUtc?: UtcDateTime | null;
    /** Prioridad del triage más reciente, si lo hay (proyección de lectura). */
    latestTriagePriority: TriagePriority | null;
}

/** Crear atención: con paciente identificado o sin patientId (NN). La web no crea Prehospital. */
export interface CreateEncounterRequest {
    type: ClinicalEncounterType;
    patientId: Guid | null;
    reason: string | null;
    /** Solo si el usuario tiene varias clínicas; el backend lo valida contra sus afiliaciones. */
    clinicId?: Guid | null;
}

/**
 * Proyección administrativa de Recepción (Patients.Identify). Sin contenido clínico: ni motivo, ni triage, ni signos
 * vitales, notas, diagnósticos, resultados o prescripciones.
 */
export interface AdmissionEncounterSummary {
    encounterId: Guid;
    patientId: Guid | null;
    patientDisplayName: string | null;
    temporaryPatientCode: string | null;
    type: ClinicalEncounterType;
    status: ClinicalEncounterStatus;
    startedAtUtc: UtcDateTime;
    clinicId: Guid | null;
}

/** Cierre de la atención (Encounters.Close). La UI lo llama "Egreso". */
export interface CloseEncounterRequest {
    /** Nota de egreso opcional; se registra como ClinicalNote de tipo Discharge. */
    dischargeNote: string | null;
}

// ---------------------------------------------------------------- Triage y signos vitales

/** Escala de cinco niveles; Immediate es la más urgente. */
export type TriagePriority = 'Immediate' | 'VeryUrgent' | 'Urgent' | 'Standard' | 'NonUrgent';

export type VitalSignType =
    | 'HeartRate'
    | 'RespiratoryRate'
    | 'SystolicBloodPressure'
    | 'DiastolicBloodPressure'
    | 'Temperature'
    | 'OxygenSaturation'
    | 'Weight'
    | 'Height'
    | 'BloodGlucose'
    | 'PainScore'
    | 'GlasgowComaScale';

/**
 * Una medición enviada al servidor. Sin `unit`: la unidad la fija el Domain (VitalSignLimits) y el BFF rechaza el campo.
 * Quién la tomó lo determina el backend a partir de la sesión.
 */
export interface VitalSignInput {
    type: VitalSignType;
    value: number;
    measuredAtUtc: UtcDateTime;
}

export interface VitalSign extends VitalSignInput {
    id: Guid;
    triageId: Guid | null;
    /** Unidad UCUM asignada por el backend. */
    unit: string;
}

/** Triage pertenece al ClinicalEncounter, no al Patient. */
export interface CreateTriageRequest {
    priority: TriagePriority;
    reason: string | null;
    observations: string | null;
    /** Si se omite, el backend usa la hora actual. */
    performedAtUtc?: UtcDateTime | null;
    /** Solo las mediciones que el usuario introdujo; puede ir vacío. Requiere además VitalSigns.Write. */
    measurements: VitalSignInput[];
}

export interface Triage {
    id: Guid;
    encounterId: Guid;
    priority: TriagePriority;
    reason: string | null;
    observations: string | null;
    performedAtUtc: UtcDateTime;
}

// ---------------------------------------------------------------- Notas y diagnósticos

export type ClinicalNoteType = 'Assessment' | 'Evolution' | 'Prehospital' | 'Discharge' | 'TransferSummary' | 'Other';

/** El autor (AuthorStaffId) lo asigna el backend desde la sesión; el cliente no lo envía. */
export interface CreateClinicalNoteRequest {
    type: ClinicalNoteType;
    content: string;
    occurredAtUtc: UtcDateTime;
}

export interface ClinicalNote extends CreateClinicalNoteRequest {
    id: Guid;
    encounterId?: Guid;
    /** ClinicalStaff autor, asignado por el backend desde la sesión. */
    authorStaffId?: Guid;
    authorDisplayName: string | null;
    createdAtUtc: UtcDateTime;
}

export type DiagnosisType = 'Presumptive' | 'Definitive';

export interface CreateDiagnosisRequest {
    code: string | null;
    codeSystem: string | null;
    description: string;
    type: DiagnosisType;
    isPrimary: boolean;
}

export type ClinicalProcedureStatus = 'Planned' | 'InProgress' | 'Completed' | 'Cancelled';

/**
 * Procedimiento de la atención (GET /encounters/{id}/procedures, solo lectura en la web). Hoy los registra el paramédico
 * (Mobile BFF) y viajan con la MISMA atención; `prehospital` indica que vienen de la ambulancia.
 */
export interface ClinicalProcedure {
    id: Guid;
    encounterId: Guid;
    name: string;
    codeSystem: string | null;
    code: string | null;
    status: ClinicalProcedureStatus;
    performedAtUtc: UtcDateTime | null;
    completedAtUtc: UtcDateTime | null;
    outcome: string | null;
    /** Nombre de quien lo realizó (personal clínico), resuelto por el backend. */
    performedByDisplayName: string | null;
    prehospital: boolean;
}

export interface Diagnosis extends CreateDiagnosisRequest {
    id: Guid;
    encounterId?: Guid;
    recordedAtUtc: UtcDateTime;
    /** Fecha de confirmación; solo en diagnósticos Definitive. */
    confirmedAtUtc?: UtcDateTime | null;
    recordedByStaffId?: Guid;
}

// ---------------------------------------------------------------- Medicamentos

export type MedicationRoute =
    | 'Oral'
    | 'Intravenous'
    | 'Intramuscular'
    | 'Subcutaneous'
    | 'Inhalation'
    | 'Topical'
    | 'Sublingual'
    | 'Rectal'
    | 'Nasal'
    | 'Ophthalmic'
    | 'Otic'
    | 'Transdermal'
    | 'Other';

export type MedicationStatus = 'Active' | 'Inactive';

/** Entrada del catálogo de medicamentos. */
export interface Medication {
    id: Guid;
    code: string | null;
    name: string;
    presentation: string | null;
    concentration: string | null;
    defaultRoute: MedicationRoute | null;
    status: MedicationStatus;
}

export type PrescriptionStatus = 'Draft' | 'Issued' | 'Cancelled';

/** Línea de una prescripción: siempre referencia un Medication del catálogo. */
export interface PrescriptionDetailInput {
    medicationId: Guid;
    dose: number;
    doseUnit: string;
    route: MedicationRoute;
    frequency: string;
    durationDays: number | null;
    instructions: string | null;
}

export interface PrescriptionDetail extends PrescriptionDetailInput {
    id: Guid;
    medicationName: string;
}

/** El backend crea, añade las líneas y emite la prescripción en una sola transacción; el prescriptor sale de la sesión. */
export interface CreatePrescriptionRequest {
    notes: string | null;
    details: PrescriptionDetailInput[];
}

export interface Prescription {
    id: Guid;
    encounterId: Guid;
    status: PrescriptionStatus;
    notes: string | null;
    createdAtUtc: UtcDateTime;
    issuedAtUtc: UtcDateTime | null;
    validUntilUtc?: UtcDateTime | null;
    cancelledAtUtc?: UtcDateTime | null;
    prescribedByStaffId?: Guid;
    details: PrescriptionDetail[];
}

export type MedicationAdministrationStatus = 'Completed' | 'EnteredInError';

/**
 * Administración de un medicamento (Kardex/MAR). Entidad propia: puede vincularse a una línea de prescripción o
 * registrarse sin ella. El responsable lo asigna el backend desde la sesión.
 */
export interface MedicationAdministration {
    id: Guid;
    encounterId: Guid;
    medicationId: Guid;
    medicationName: string;
    prescriptionId: Guid | null;
    prescriptionDetailId: Guid | null;
    dose: number;
    doseUnit: string;
    route: MedicationRoute;
    administeredAtUtc: UtcDateTime;
    administeredByStaffId?: Guid;
    status: MedicationAdministrationStatus;
    notes: string | null;
}

/**
 * Registro de una dosis administrada. Si se vincula a una línea de prescripción (prescriptionId + prescriptionDetailId),
 * el medicamento debe ser el prescrito. Quién la administró lo fija el backend desde la sesión.
 */
export interface CreateMedicationAdministrationRequest {
    medicationId: Guid;
    dose: number;
    doseUnit: string;
    route: MedicationRoute;
    administeredAtUtc: UtcDateTime;
    prescriptionId: Guid | null;
    prescriptionDetailId: Guid | null;
    notes: string | null;
}

// ---------------------------------------------------------------- Organizaciones y transporte

export interface AmbulanceSummary {
    id: Guid;
    code: string;
    callSign: string | null;
}

export type ClinicalTransferStatus = 'Requested' | 'InTransit' | 'Received' | 'Completed' | 'Cancelled';

/** ClinicalTransfer visto desde el hospital receptor. La captura de campo pertenece a la app paramédica (Rol 6). */
export interface ClinicalTransfer {
    id: Guid;
    encounterId: Guid;
    status: ClinicalTransferStatus;
    originClinic: ClinicSummary | null;
    destinationClinic: ClinicSummary;
    ambulance: AmbulanceSummary | null;
    requestedAtUtc: UtcDateTime;
    sentAtUtc: UtcDateTime | null;
    sentByStaffId?: Guid | null;
    receivedAtUtc: UtcDateTime | null;
    receivedByStaffId?: Guid | null;
    cancelledAtUtc?: UtcDateTime | null;
    notes?: string | null;
    /** Estado de la atención trasladada (Transferred mientras va en camino). */
    encounterStatus?: ClinicalEncounterStatus;
    patient: PatientSummary | null;
    temporaryPatientCode: string | null;
    reason: string | null;
    /** Prioridad del triage prehospitalario, si existe. */
    latestTriagePriority: TriagePriority | null;
}

/** Clínica activa a la que se puede trasladar al paciente (GET /api/web/transfers/destinations). Sin datos de administración. */
export interface TransferDestination {
    id: Guid;
    name: string;
    type: string;
    municipality: string;
    province: string;
}

/** POST /api/web/encounters/{id}/transfers: la clínica de origen la fija el backend (la de la atención). */
export interface RequestTransferRequest {
    destinationClinicId: Guid;
    reason?: string | null;
    notes?: string | null;
}

// ---- Huella digital (ADR-010) ----

/** Lector donde se puede registrar una huella: activo, que registra e identifica. Sin claves ni scopes. */
export interface FingerprintEnroller {
    id: Guid;
    code: string;
    name: string;
    lastSeenAtUtc: string | null;
}

export type BiometricReferenceStatus = 'Active' | 'Revoked';

/**
 * Limpieza física de una huella revocada en su lector (ADR-011). Derivada por el servidor; nunca incluye el slot.
 * Pending: el lector aún debe borrar su copia. Completed: borrada. AttentionRequired: el lector no puede hacerlo (revocado,
 * desconocido) o sigue fallando. NotRequired: revocada antes de existir la limpieza automática.
 */
export type FingerprintCleanupStatus = 'Pending' | 'Completed' | 'AttentionRequired' | 'NotRequired';

/** Referencia de huella del paciente. Nunca incluye el slot ni la referencia interna del sensor. */
export interface PatientFingerprint {
    id: Guid;
    status: BiometricReferenceStatus;
    /** null si la referencia no nombra un lector conocido. available: el lector sigue activo y puede identificar. */
    reader: { code: string; name: string; available: boolean } | null;
    registeredAtUtc: string;
    revokedAtUtc: string | null;
    /** null mientras está activa. */
    cleanup: FingerprintCleanupStatus | null;
}

/** Motivo opcional de la revocación; solo queda en la auditoría. */
export type FingerprintRevocationReason = 'PatientRequest' | 'EnrollmentError' | 'ReaderReplaced' | 'Other';

/**
 * Referencia facial del paciente (ADR-012): verificación 1:1 asistida. Nunca incluye la referencia del proveedor, la
 * plantilla, el puntaje ni el umbral. provider: nombre estable del proveedor (la UI muestra una etiqueta).
 */
export interface PatientFace {
    id: Guid;
    status: BiometricReferenceStatus;
    provider: string;
    registeredAtUtc: string;
    revokedAtUtc: string | null;
}

export type FaceEnrollmentOutcome = 'Enrolled' | 'NoFace' | 'MultipleFaces' | 'LowQuality';

/** face: solo con Enrolled. Las demás respuestas no crean nada. */
export interface FaceEnrollmentResponse {
    outcome: FaceEnrollmentOutcome;
    face: PatientFace | null;
}

export type FaceVerificationOutcome = 'Match' | 'NoMatch' | 'NoFace' | 'MultipleFaces' | 'LowQuality';

/** Solo el resultado: nunca un puntaje ni un umbral. */
export interface FaceVerificationResponse {
    verificationId: Guid;
    outcome: FaceVerificationOutcome;
}

export type FaceRevocationReason = 'PatientRequest' | 'EnrollmentError' | 'ReferenceOutdated' | 'Other';

export type FingerprintEnrollmentStatus = 'Pending' | 'Enrolled' | 'Failed' | 'Expired' | 'Cancelled';

export interface FingerprintEnrollment {
    id: Guid;
    status: FingerprintEnrollmentStatus;
    readerId: Guid;
    expiresAtUtc: string;
    /** Solo con Failed. */
    failure: 'Failed' | 'LowQuality' | 'SensorError' | null;
    biometricReferenceId: Guid | null;
}

/** Tipos de punto a revisar: solo sobre la DOCUMENTACIÓN, nunca un juicio clínico (ADR-013). */
export type EncounterAiReviewPointKind = 'MissingDocumentation' | 'Inconsistency' | 'PendingItem';

export type ClinicalAiSourceType =
    | 'Triage'
    | 'VitalSign'
    | 'ClinicalNote'
    | 'Diagnosis'
    | 'Prescription'
    | 'MedicationAdministration'
    | 'ClinicalProcedure'
    | 'ClinicalTransfer';

/** Registro relacionado: el servidor comprobó que existe en esta atención, NO que respalde la frase. */
export interface EncounterAiSourceReference {
    type: ClinicalAiSourceType;
    id: Guid;
}

export interface EncounterAiSection {
    text: string;
    sources: EncounterAiSourceReference[];
}

export interface EncounterAiReviewPoint {
    kind: EncounterAiReviewPointKind;
    text: string;
    sources: EncounterAiSourceReference[];
}

/**
 * Resumen asistido de una atención (ADR-013): generado por IA, requiere revisión profesional, efímero (no se guarda) e
 * instantánea de los registros en generatedAtUtc. Sin diagnóstico, recomendación, tratamiento, gravedad ni confianza.
 */
export interface EncounterAiSummary {
    aiGenerated: true;
    requiresClinicianReview: true;
    summary: string;
    sections: {
        course: EncounterAiSection | null;
        vitalSigns: EncounterAiSection | null;
        medications: EncounterAiSection | null;
        procedures: EncounterAiSection | null;
        notes: EncounterAiSection | null;
        transfer: EncounterAiSection | null;
    };
    reviewPoints: EncounterAiReviewPoint[];
    notDocumented: string[];
    generatedAtUtc: string;
    provider: string;
    model: string;
    promptVersion: string;
    sourceFingerprint: string;
    truncated: boolean;
}
