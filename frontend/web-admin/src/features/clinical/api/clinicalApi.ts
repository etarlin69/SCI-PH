import { ApiError, NetworkError, describeApiError } from '../../../api';
import type {
    AdmissionEncounterSummary,
    ClinicalContext,
    ClinicalEncounterSummary,
    ClinicalNote,
    ClinicalTransfer,
    CloseEncounterRequest,
    CreateClinicalNoteRequest,
    CreateDiagnosisRequest,
    CreateEncounterRequest,
    CreateMedicationAdministrationRequest,
    CreatePrescriptionRequest,
    CreateTriageRequest,
    Diagnosis,
    Guid,
    Medication,
    MedicationAdministration,
    PatientSearchRequest,
    PatientSummary,
    Prescription,
    RegisterPatientRequest,
    Triage,
    VitalSign,
    VitalSignInput,
} from '../types/clinical.types';

/**
 * Puerto de datos del frontend hospitalario. Dos adaptadores: `httpClinicalApi` (Web BFF real con `apiRequest` de
 * src/api: Bearer en memoria, refresh single-flight, ProblemDetails) y `mockClinicalApi` (datos DEMO en memoria) para
 * lo que aún no tiene backend. Este módulo no maneja tokens ni cookies. Rutas: docs/HOSPITAL_CONTRATOS.md.
 */
export interface ClinicalApi {
    /** Clínicas donde el usuario actúa como ClinicalStaff. 403 si no es personal clínico activo. */
    getClinicalContext(signal?: AbortSignal): Promise<ClinicalContext>;

    searchPatients(request: PatientSearchRequest, signal?: AbortSignal): Promise<PatientSummary[]>;
    registerPatient(request: RegisterPatientRequest): Promise<PatientSummary>;

    /** Atenciones abiertas de la clínica activa (ni Closed ni Cancelled, ni en traslado). */
    listActiveEncounters(signal?: AbortSignal): Promise<ClinicalEncounterSummary[]>;
    getEncounter(encounterId: Guid, signal?: AbortSignal): Promise<ClinicalEncounterSummary>;
    createEncounter(request: CreateEncounterRequest): Promise<ClinicalEncounterSummary>;
    /** Vincula una atención NN al paciente real; el código NN se conserva como historial. */
    identifyEncounterPatient(encounterId: Guid, patientId: Guid): Promise<void>;
    closeEncounter(encounterId: Guid, request: CloseEncounterRequest): Promise<void>;

    /** Proyección administrativa de Recepción (sin datos clínicos). */
    listAdmissionEncounters(options?: { unidentifiedOnly?: boolean }, signal?: AbortSignal): Promise<AdmissionEncounterSummary[]>;

    listTriages(encounterId: Guid, signal?: AbortSignal): Promise<Triage[]>;
    createTriage(encounterId: Guid, request: CreateTriageRequest): Promise<Triage>;
    listVitalSigns(encounterId: Guid, signal?: AbortSignal): Promise<VitalSign[]>;
    createVitalSign(encounterId: Guid, request: VitalSignInput): Promise<VitalSign>;

    listNotes(encounterId: Guid, signal?: AbortSignal): Promise<ClinicalNote[]>;
    createNote(encounterId: Guid, request: CreateClinicalNoteRequest): Promise<ClinicalNote>;
    listDiagnoses(encounterId: Guid, signal?: AbortSignal): Promise<Diagnosis[]>;
    createDiagnosis(encounterId: Guid, request: CreateDiagnosisRequest): Promise<Diagnosis>;

    /** Catálogo global (activos e inactivos). */
    listMedications(signal?: AbortSignal): Promise<Medication[]>;
    listPrescriptions(encounterId: Guid, signal?: AbortSignal): Promise<Prescription[]>;
    /** Crea y emite la prescripción con sus líneas en una sola operación. */
    createPrescription(encounterId: Guid, request: CreatePrescriptionRequest): Promise<Prescription>;
    /** Kardex: administraciones registradas (las prescripciones se leen con listPrescriptions). */
    listMedicationAdministrations(encounterId: Guid, signal?: AbortSignal): Promise<MedicationAdministration[]>;
    createMedicationAdministration(encounterId: Guid, request: CreateMedicationAdministrationRequest): Promise<MedicationAdministration>;

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

// Mensajes de reglas clínicas conocidas del Web BFF (ProblemDetails.detail, en inglés) traducidos para la UI.
const KNOWN_DETAILS: [RegExp, string][] = [
    [/not registered as clinical staff/i, 'Tu usuario no está registrado como personal clínico.'],
    [/clinical staff record .* is inactive/i, 'Tu registro de personal clínico está inactivo.'],
    [/no active clinic affiliation/i, 'No tienes una afiliación activa a ninguna clínica.'],
    [/not affiliated with the requested clinic|belongs to a clinic the user is not affiliated/i, 'El registro pertenece a una clínica a la que no estás afiliado.'],
    [/clinicId is required/i, 'Selecciona la clínica en la que estás trabajando.'],
    [/cannot be in the future/i, 'La fecha y hora no pueden estar en el futuro.'],
    [/cannot be earlier than the start of the encounter/i, 'La fecha y hora no pueden ser anteriores al inicio de la atención.'],
    [/must be between/i, 'El valor está fuera del rango admitido para ese signo vital.'],
    [/requires an identified patient/i, 'Este tipo de atención requiere un paciente identificado.'],
    [/already linked to a patient/i, 'La atención ya fue identificada.'],
    [/does not accept clinical records/i, 'La atención está cerrada o en un estado que no admite registros.'],
    [/cannot be closed/i, 'La atención no puede cerrarse en su estado actual.'],
    [/notes cannot be written from this endpoint/i, 'Ese tipo de nota no se registra desde aquí.'],
    [/code requires its code system/i, 'Indica el sistema de codificación del código (p. ej. CIE-10).'],
    [/is not active in the catalog/i, 'El medicamento no está activo en el catálogo.'],
    [/needs at least one medication/i, 'Añade al menos un medicamento a la prescripción.'],
    [/cannot be administered/i, 'La prescripción no está emitida o fue anulada; no admite administraciones.'],
    [/no longer valid/i, 'La prescripción ya no está vigente.'],
    [/medication of the prescription detail|is not part of the prescription/i, 'El medicamento no coincide con la línea de la prescripción.'],
    [/^Medication .* was not found/i, 'El medicamento no existe en el catálogo.'],
    [/^Prescription(Detail)? .* was not found/i, 'La prescripción no existe en esta atención.'],
    [/Receive requires a InTransit transfer/i, 'El traslado ya fue recibido o todavía no está en camino.'],
    [/transfer does not involve a clinic/i, 'El traslado no tiene como origen ni destino una clínica a la que estés afiliado.'],
    [/^ClinicalTransfer .* was not found/i, 'El traslado no existe.'],
    [/changed by another operation/i, 'Otro usuario modificó este registro. Recarga e inténtalo de nuevo.'],
    [/identifier with this value already belongs/i, 'Ya existe un paciente con ese identificador.'],
    [/needs at least 3 characters/i, 'Escribe al menos 3 caracteres para buscar por nombre.'],
];

/**
 * Mensaje legible para la UI. No incluye datos clínicos. Acepta los errores del mock (ClinicalApiError) y los del
 * cliente HTTP compartido (ApiError/NetworkError de src/api).
 */
export function describeError(error: unknown): string {
    if (error instanceof ApiError || error instanceof NetworkError) {
        const detail = error instanceof ApiError ? error.problem?.detail : undefined;
        const known = typeof detail === 'string' ? KNOWN_DETAILS.find(([pattern]) => pattern.test(detail)) : undefined;
        if (known) return known[1];
        return describeApiError(error, { 404: 'El registro no existe o no es visible para tu usuario.' });
    }
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
