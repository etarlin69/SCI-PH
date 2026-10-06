import type {
    ClinicalEncounterSummary,
    ClinicalNote,
    ClinicalTransfer,
    CloseEncounterRequest,
    CreateClinicalNoteRequest,
    CreateDiagnosisRequest,
    CreateEncounterRequest,
    CreatePrescriptionRequest,
    CreateTriageRequest,
    Diagnosis,
    Guid,
    Medication,
    MedicationAdministration,
    PatientSearchRequest,
    PatientSummary,
    Prescription,
    Triage,
    VitalSign,
} from '../types/clinical.types';

/**
 * Puerto de datos del frontend hospitalario. Hoy solo existe el adaptador MOCK (mockClinicalApi.ts) porque los
 * endpoints clínicos del Web BFF no están implementados. El adaptador HTTP se escribirá sobre el cliente común del
 * Rol 5 (token en memoria, refresh por cookie, CSRF): este módulo no maneja tokens ni cookies.
 * Rutas previstas: docs/HOSPITAL_CONTRATOS.md.
 */
export interface ClinicalApi {
    searchPatients(request: PatientSearchRequest, signal?: AbortSignal): Promise<PatientSummary[]>;

    /** Atenciones abiertas del establecimiento (ni Closed ni Cancelled, ni en traslado). */
    listActiveEncounters(signal?: AbortSignal): Promise<ClinicalEncounterSummary[]>;
    getEncounter(encounterId: Guid, signal?: AbortSignal): Promise<ClinicalEncounterSummary>;
    createEncounter(request: CreateEncounterRequest): Promise<ClinicalEncounterSummary>;
    closeEncounter(encounterId: Guid, request: CloseEncounterRequest): Promise<void>;

    listTriages(encounterId: Guid, signal?: AbortSignal): Promise<Triage[]>;
    createTriage(encounterId: Guid, request: CreateTriageRequest): Promise<Triage>;
    listVitalSigns(encounterId: Guid, signal?: AbortSignal): Promise<VitalSign[]>;

    listNotes(encounterId: Guid, signal?: AbortSignal): Promise<ClinicalNote[]>;
    createNote(encounterId: Guid, request: CreateClinicalNoteRequest): Promise<ClinicalNote>;
    listDiagnoses(encounterId: Guid, signal?: AbortSignal): Promise<Diagnosis[]>;
    createDiagnosis(encounterId: Guid, request: CreateDiagnosisRequest): Promise<Diagnosis>;

    listMedications(signal?: AbortSignal): Promise<Medication[]>;
    listPrescriptions(encounterId: Guid, signal?: AbortSignal): Promise<Prescription[]>;
    createPrescription(encounterId: Guid, request: CreatePrescriptionRequest): Promise<Prescription>;
    listMedicationAdministrations(encounterId: Guid, signal?: AbortSignal): Promise<MedicationAdministration[]>;

    listIncomingTransfers(signal?: AbortSignal): Promise<ClinicalTransfer[]>;
    receiveTransfer(transferId: Guid): Promise<void>;
}

/** Error de la capa clínica. `status` sigue la semántica HTTP (401, 403, 404, 409, 500…) aunque venga del mock. */
export class ClinicalApiError extends Error {
    readonly status: number;

    constructor(status: number, message: string) {
        super(message);
        this.name = 'ClinicalApiError';
        this.status = status;
    }
}

/** Mensaje legible para la UI. No incluye datos clínicos. */
export function describeError(error: unknown): string {
    if (error instanceof ClinicalApiError) {
        switch (error.status) {
            case 401:
                return 'Tu sesión no es válida o expiró. Vuelve a iniciar sesión.';
            case 403:
                return 'No tienes permiso para esta acción.';
            case 404:
                return 'El registro no existe o no es visible para tu usuario.';
            case 409:
                return error.message || 'La acción no es válida en el estado actual del registro.';
            default:
                return error.message || 'Ocurrió un error inesperado.';
        }
    }
    return 'Ocurrió un error inesperado.';
}
