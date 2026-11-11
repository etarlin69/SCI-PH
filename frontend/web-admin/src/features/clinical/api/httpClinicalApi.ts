import { apiRequest } from '../../../api';
import type {
    AdmissionEncounterSummary,
    ClinicalContext,
    ClinicalEncounterSummary,
    ClinicalNote,
    ClinicalProcedure,
    ClinicalTransfer,
    TransferDestination,
    Diagnosis,
    EncounterAiSummary,
    FingerprintEnroller,
    FingerprintEnrollment,
    FaceEnrollmentResponse,
    FaceVerificationResponse,
    PatientFace,
    PatientFingerprint,
    Medication,
    MedicationAdministration,
    PatientSummary,
    Prescription,
    PrescriptionDetailInput,
    Triage,
    VitalSign,
    VitalSignInput,
} from '../types/clinical.types';
import type { ClinicalApi } from './clinicalApi';
import { clinicIdForRequest } from './clinicSelection';

// Adaptador HTTP real: React Hospital → apiRequest → Web BFF → Application → ClinicalDbContext → PostgreSQL.
// Van al backend: pacientes, atenciones, admisión, triage, signos vitales (vertical 1); notas clínicas, diagnósticos y
// cierre de la atención (vertical 2); catálogo de medicamentos, prescripciones y Kardex (vertical 3); traslados entrantes
// y su recepción (vertical 4). El adaptador MOCK solo se usa con VITE_CLINICAL_API_MODE=mock.

const BASE = '/api/web';
const id = (value: string) => encodeURIComponent(value);

// multipart/form-data con exactamente dos partes: la captura y su clave. Nada más (el servidor rechaza cualquier otra).
const faceForm = (capture: Blob, clientRequestId: string) => {
    const form = new FormData();
    form.append('capture', capture, 'capture.jpg');
    form.append('clientRequestId', clientRequestId);
    return form;
};

// Solo los campos del contrato: el BFF rechaza miembros desconocidos (p. ej. `unit`, que fija el Domain).
const toMeasurement = ({ type, value, measuredAtUtc }: VitalSignInput): VitalSignInput => ({ type, value, measuredAtUtc });
const toPrescriptionLine = ({ medicationId, dose, doseUnit, route, frequency, durationDays, instructions }: PrescriptionDetailInput): PrescriptionDetailInput => ({
    medicationId,
    dose,
    doseUnit,
    route,
    frequency,
    durationDays,
    instructions,
});

export const httpClinicalApi: ClinicalApi = {
    getClinicalContext: (signal) => apiRequest<ClinicalContext>(`${BASE}/clinical/context`, { signal }),

    // Identificadores siempre en el cuerpo (POST), nunca en la URL.
    searchPatients: (request, signal) =>
        apiRequest<PatientSummary[]>(`${BASE}/patients/search`, { method: 'POST', body: request, signal }),

    registerPatient: (request) => apiRequest<PatientSummary>(`${BASE}/patients`, { method: 'POST', body: request }),

    getPatient: (patientId, signal) => apiRequest<PatientSummary>(`${BASE}/patients/${id(patientId)}`, { signal }),

    listFingerprintEnrollers: (signal) => apiRequest<FingerprintEnroller[]>(`${BASE}/fingerprint-enrollers`, { signal }),

    listPatientFingerprints: (patientId, signal) =>
        apiRequest<PatientFingerprint[]>(`${BASE}/patients/${id(patientId)}/fingerprints`, { signal }),

    // Solo clave y lector: el paciente va en la ruta y el personal sale de la sesión.
    openFingerprintEnrollment: (patientId, { clientRequestId, deviceId }) =>
        apiRequest<FingerprintEnrollment>(`${BASE}/patients/${id(patientId)}/fingerprints/enrollments`, {
            method: 'POST',
            body: { clientRequestId, deviceId },
        }),

    getFingerprintEnrollment: (patientId, enrollmentId, signal) =>
        apiRequest<FingerprintEnrollment>(`${BASE}/patients/${id(patientId)}/fingerprints/enrollments/${id(enrollmentId)}`, { signal }),

    cancelFingerprintEnrollment: (patientId, enrollmentId) =>
        apiRequest<FingerprintEnrollment>(`${BASE}/patients/${id(patientId)}/fingerprints/enrollments/${id(enrollmentId)}/cancel`, {
            method: 'POST',
        }),

    listPatientFaces: (patientId, signal) => apiRequest<PatientFace[]>(`${BASE}/patients/${id(patientId)}/faces`, { signal }),

    // Solo la captura y su clave (multipart): paciente en la ruta; proveedor, umbral y referencia los decide el servidor.
    enrollPatientFace: (patientId, capture, clientRequestId) =>
        apiRequest<FaceEnrollmentResponse>(`${BASE}/patients/${id(patientId)}/faces/enroll`, { method: 'POST', body: faceForm(capture, clientRequestId) }),

    verifyPatientFace: (patientId, capture, clientRequestId) =>
        apiRequest<FaceVerificationResponse>(`${BASE}/patients/${id(patientId)}/faces/verify`, { method: 'POST', body: faceForm(capture, clientRequestId) }),

    revokePatientFace: (patientId, referenceId, reason) =>
        apiRequest<PatientFace>(`${BASE}/patients/${id(patientId)}/faces/${id(referenceId)}/revoke`, { method: 'POST', body: reason ? { reason } : {} }),

    // Solo el motivo (opcional): paciente y referencia van en la ruta; el personal y la fecha los pone el servidor.
    revokePatientFingerprint: (patientId, referenceId, reason) =>
        apiRequest<PatientFingerprint>(`${BASE}/patients/${id(patientId)}/fingerprints/${id(referenceId)}/revoke`, {
            method: 'POST',
            body: reason ? { reason } : {},
        }),

    listActiveEncounters: (signal) =>
        apiRequest<ClinicalEncounterSummary[]>(`${BASE}/encounters`, { query: { clinicId: clinicIdForRequest() }, signal }),

    getEncounter: (encounterId, signal) => apiRequest<ClinicalEncounterSummary>(`${BASE}/encounters/${id(encounterId)}`, { signal }),

    createEncounter: (request) =>
        apiRequest<ClinicalEncounterSummary>(`${BASE}/encounters`, {
            method: 'POST',
            body: { ...request, clinicId: request.clinicId ?? clinicIdForRequest() ?? null },
        }),

    identifyEncounterPatient: (encounterId, patientId) =>
        apiRequest<void>(`${BASE}/encounters/${id(encounterId)}/identify`, { method: 'POST', body: { patientId } }),

    listAdmissionEncounters: (options, signal) =>
        apiRequest<AdmissionEncounterSummary[]>(`${BASE}/admissions/encounters`, {
            query: { clinicId: clinicIdForRequest(), unidentified: options?.unidentifiedOnly ? 'true' : undefined },
            signal,
        }),

    listTriages: (encounterId, signal) => apiRequest<Triage[]>(`${BASE}/encounters/${id(encounterId)}/triages`, { signal }),

    createTriage: (encounterId, { priority, reason, observations, performedAtUtc, measurements }) =>
        apiRequest<Triage>(`${BASE}/encounters/${id(encounterId)}/triages`, {
            method: 'POST',
            body: { priority, reason, observations, performedAtUtc: performedAtUtc ?? undefined, measurements: measurements.map(toMeasurement) },
        }),

    listVitalSigns: (encounterId, signal) => apiRequest<VitalSign[]>(`${BASE}/encounters/${id(encounterId)}/vital-signs`, { signal }),

    createVitalSign: (encounterId, request) =>
        apiRequest<VitalSign>(`${BASE}/encounters/${id(encounterId)}/vital-signs`, { method: 'POST', body: toMeasurement(request) }),

    // El autor y la fecha de registro los fija el backend desde la sesión: solo se envían los campos del contrato.
    listNotes: (encounterId, signal) => apiRequest<ClinicalNote[]>(`${BASE}/encounters/${id(encounterId)}/notes`, { signal }),

    createNote: (encounterId, { type, content, occurredAtUtc }) =>
        apiRequest<ClinicalNote>(`${BASE}/encounters/${id(encounterId)}/notes`, { method: 'POST', body: { type, content, occurredAtUtc } }),

    listDiagnoses: (encounterId, signal) => apiRequest<Diagnosis[]>(`${BASE}/encounters/${id(encounterId)}/diagnoses`, { signal }),

    createDiagnosis: (encounterId, { description, type, isPrimary, codeSystem, code }) =>
        apiRequest<Diagnosis>(`${BASE}/encounters/${id(encounterId)}/diagnoses`, {
            method: 'POST',
            body: { description, type, isPrimary, codeSystem, code },
        }),

    // Solo lectura: los registra el paramédico y viajan con la misma atención.
    listProcedures: (encounterId, signal) => apiRequest<ClinicalProcedure[]>(`${BASE}/encounters/${id(encounterId)}/procedures`, { signal }),

    // ClinicalEncounter.ChangeStatus(Closed); la nota de egreso opcional se guarda como ClinicalNote Discharge.
    closeEncounter: (encounterId, { dischargeNote }) =>
        apiRequest<void>(`${BASE}/encounters/${id(encounterId)}/close`, { method: 'POST', body: { dischargeNote } }),

    // Catálogo global de medicamentos (Medications.Read).
    listMedications: (signal) => apiRequest<Medication[]>(`${BASE}/medications`, { signal }),

    listPrescriptions: (encounterId, signal) => apiRequest<Prescription[]>(`${BASE}/encounters/${id(encounterId)}/prescriptions`, { signal }),

    // Prescription.Draft + AddDetail + Issue en una transacción del backend; el prescriptor sale de la sesión.
    createPrescription: (encounterId, { notes, details }) =>
        apiRequest<Prescription>(`${BASE}/encounters/${id(encounterId)}/prescriptions`, {
            method: 'POST',
            body: { notes, details: details.map(toPrescriptionLine) },
        }),

    // Kardex: las administraciones reales; quién administró lo fija el backend.
    listMedicationAdministrations: (encounterId, signal) =>
        apiRequest<MedicationAdministration[]>(`${BASE}/encounters/${id(encounterId)}/medication-administrations`, { signal }),

    createMedicationAdministration: (
        encounterId,
        { medicationId, dose, doseUnit, route, administeredAtUtc, prescriptionId, prescriptionDetailId, notes },
    ) =>
        apiRequest<MedicationAdministration>(`${BASE}/encounters/${id(encounterId)}/medication-administrations`, {
            method: 'POST',
            body: { medicationId, dose, doseUnit, route, administeredAtUtc, prescriptionId, prescriptionDetailId, notes },
        }),

    // Resumen asistido (ADR-013): POST sin cuerpo; el servidor decide proveedor, modelo, prompt y límites.
    generateEncounterAiSummary: (encounterId, signal) =>
        apiRequest<EncounterAiSummary>(`${BASE}/encounters/${id(encounterId)}/ai-summary`, { method: 'POST', signal }),

    // Traslados con destino a las clínicas del usuario (Requested / InTransit).
    listIncomingTransfers: (signal) =>
        apiRequest<ClinicalTransfer[]>(`${BASE}/transfers/incoming`, { query: { clinicId: clinicIdForRequest() }, signal }),

    // ClinicalTransfer.Receive + la atención pasa a la clínica destino; quién recibe lo fija el backend desde la sesión.
    receiveTransfer: async (transferId) => {
        await apiRequest<ClinicalTransfer>(`${BASE}/transfers/${id(transferId)}/receive`, { method: 'POST' });
    },

    // Activos e históricos de la atención; con los mismos permisos de lectura que la atención.
    listEncounterTransfers: (encounterId, signal) =>
        apiRequest<ClinicalTransfer[]>(`${BASE}/encounters/${id(encounterId)}/transfers`, { signal }),

    // Destinos desde la clínica de la atención (el backend la valida y la excluye); no es el catálogo de administración.
    listTransferDestinations: (fromClinicId, signal) =>
        apiRequest<TransferDestination[]>(`${BASE}/transfers/destinations`, { query: { clinicId: fromClinicId ?? clinicIdForRequest() }, signal }),

    // El origen es la clínica de la atención y quién solicita lo fija el backend desde la sesión.
    requestTransfer: (encounterId, request) =>
        apiRequest<ClinicalTransfer>(`${BASE}/encounters/${id(encounterId)}/transfers`, {
            method: 'POST',
            body: { destinationClinicId: request.destinationClinicId, reason: request.reason ?? null, notes: request.notes ?? null },
        }),

    dispatchTransfer: (transferId) => apiRequest<ClinicalTransfer>(`${BASE}/transfers/${id(transferId)}/dispatch`, { method: 'POST' }),

    cancelTransfer: (transferId) => apiRequest<ClinicalTransfer>(`${BASE}/transfers/${id(transferId)}/cancel`, { method: 'POST' }),
};
