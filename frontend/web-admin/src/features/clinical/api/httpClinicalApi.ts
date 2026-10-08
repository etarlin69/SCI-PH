import { apiRequest } from '../../../api';
import type {
    AdmissionEncounterSummary,
    ClinicalContext,
    ClinicalEncounterSummary,
    ClinicalNote,
    Diagnosis,
    PatientSummary,
    Triage,
    VitalSign,
    VitalSignInput,
} from '../types/clinical.types';
import type { ClinicalApi } from './clinicalApi';
import { clinicIdForRequest } from './clinicSelection';
import { mockClinicalApi } from './mockClinicalApi';

// Adaptador HTTP real: React Hospital → apiRequest → Web BFF → Application → ClinicalDbContext → PostgreSQL.
// Van al backend: pacientes, atenciones, admisión, triage, signos vitales (vertical 1) y notas clínicas, diagnósticos y
// cierre de la atención (vertical 2). El resto sigue en el MOCK (marcado «Simulación» en la UI) hasta que tenga endpoints.

const BASE = '/api/web';
const id = (value: string) => encodeURIComponent(value);

// Solo los campos del contrato: el BFF rechaza miembros desconocidos (p. ej. `unit`, que fija el Domain).
const toMeasurement = ({ type, value, measuredAtUtc }: VitalSignInput): VitalSignInput => ({ type, value, measuredAtUtc });

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

    // ---- Sin backend todavía ------------------------------------------------------------------------------------
    listMedications: mockClinicalApi.listMedications,
    listPrescriptions: mockClinicalApi.listPrescriptions,
    createPrescription: mockClinicalApi.createPrescription,
    listMedicationAdministrations: mockClinicalApi.listMedicationAdministrations,
    listIncomingTransfers: mockClinicalApi.listIncomingTransfers,
    receiveTransfer: mockClinicalApi.receiveTransfer,
};
