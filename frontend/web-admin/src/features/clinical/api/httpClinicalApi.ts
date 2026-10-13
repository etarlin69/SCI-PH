import { apiRequest } from '../../../api';
import type {
    AdmissionEncounterSummary,
    ClinicalContext,
    ClinicalEncounterSummary,
    ClinicalNote,
    ClinicalTransfer,
    Diagnosis,
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

    // Traslados con destino a las clínicas del usuario (Requested / InTransit).
    listIncomingTransfers: (signal) =>
        apiRequest<ClinicalTransfer[]>(`${BASE}/transfers/incoming`, { query: { clinicId: clinicIdForRequest() }, signal }),

    // ClinicalTransfer.Receive + la atención pasa a la clínica destino; quién recibe lo fija el backend desde la sesión.
    receiveTransfer: async (transferId) => {
        await apiRequest<ClinicalTransfer>(`${BASE}/transfers/${id(transferId)}/receive`, { method: 'POST' });
    },
};
