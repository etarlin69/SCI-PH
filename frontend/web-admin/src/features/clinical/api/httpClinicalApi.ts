import { apiRequest } from '../../../api';
import type {
    AdmissionEncounterSummary,
    ClinicalContext,
    ClinicalEncounterSummary,
    PatientSummary,
    Triage,
    VitalSign,
    VitalSignInput,
} from '../types/clinical.types';
import { ClinicalApiError, type ClinicalApi } from './clinicalApi';
import { clinicIdForRequest } from './clinicSelection';
import { mockClinicalApi } from './mockClinicalApi';

// Adaptador HTTP real: React Hospital → apiRequest → Web BFF → Application → ClinicalDbContext → PostgreSQL.
// Solo las funciones de la vertical clínica 1 van al backend (pacientes, atenciones, admisión, triage y signos vitales).
// El resto sigue en el MOCK (marcado «Simulación» en la UI) hasta que tenga endpoints.

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

    // ---- Sin backend todavía ------------------------------------------------------------------------------------
    // Cerrar una atención real no puede simularse sin desincronizar su estado: se rechaza de forma explícita.
    closeEncounter: () => Promise.reject(new ClinicalApiError(501, 'El egreso todavía no está disponible en el backend.')),

    listNotes: mockClinicalApi.listNotes,
    createNote: mockClinicalApi.createNote,
    listDiagnoses: mockClinicalApi.listDiagnoses,
    createDiagnosis: mockClinicalApi.createDiagnosis,
    listMedications: mockClinicalApi.listMedications,
    listPrescriptions: mockClinicalApi.listPrescriptions,
    createPrescription: mockClinicalApi.createPrescription,
    listMedicationAdministrations: mockClinicalApi.listMedicationAdministrations,
    listIncomingTransfers: mockClinicalApi.listIncomingTransfers,
    receiveTransfer: mockClinicalApi.receiveTransfer,
};
