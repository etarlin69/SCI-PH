// Tipos del frontend hospitalario alineados con SCI-PH.Domain (src/Core/SCI-PH.Domain).
// Los enums usan el NOMBRE del miembro C#. El Web BFF aún no publica estos contratos ni decide si serializa los enums
// como texto (JsonStringEnumConverter) o como número: ver docs/HOSPITAL_CONTRATOS.md.

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
    /** Prioridad del triage más reciente, si lo hay (proyección de lectura). */
    latestTriagePriority: TriagePriority | null;
}

/** Crear atención: con paciente identificado o sin patientId (NN). */
export interface CreateEncounterRequest {
    type: ClinicalEncounterType;
    patientId: Guid | null;
    reason: string | null;
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

/** Una medición. Quién la tomó lo determina el backend a partir de la sesión. */
export interface VitalSignInput {
    type: VitalSignType;
    value: number;
    unit: string;
    measuredAtUtc: UtcDateTime;
}

export interface VitalSign extends VitalSignInput {
    id: Guid;
    triageId: Guid | null;
}

/** Triage pertenece al ClinicalEncounter, no al Patient. */
export interface CreateTriageRequest {
    priority: TriagePriority;
    reason: string | null;
    observations: string | null;
    /** Solo las mediciones que el usuario introdujo; puede ir vacío. */
    vitalSigns: VitalSignInput[];
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

export interface Diagnosis extends CreateDiagnosisRequest {
    id: Guid;
    recordedAtUtc: UtcDateTime;
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
    status: MedicationAdministrationStatus;
    notes: string | null;
}

// ---------------------------------------------------------------- Organizaciones y transporte

export interface ClinicSummary {
    id: Guid;
    name: string;
}

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
    receivedAtUtc: UtcDateTime | null;
    patient: PatientSummary | null;
    temporaryPatientCode: string | null;
    reason: string | null;
    /** Prioridad del triage prehospitalario, si existe. */
    latestTriagePriority: TriagePriority | null;
}
