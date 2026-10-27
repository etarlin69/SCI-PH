import type {
    AdmissionEncounterSummary,
    ClinicalEncounterStatus,
    ClinicalEncounterSummary,
    ClinicalNote,
    ClinicalTransfer,
    Diagnosis,
    Guid,
    Medication,
    MedicationAdministration,
    PatientSummary,
    Prescription,
    Triage,
    VitalSign,
} from '../types/clinical.types';
import { vitalSignMeta } from '../utils/clinicalLabels';
import { ClinicalApiError, type ClinicalApi } from './clinicalApi';

// MOCK — adaptador de desarrollo visual. Todos los datos son FICTICIOS ("DEMO") y viven solo en memoria: se pierden al
// recargar y nunca se escriben en localStorage/sessionStorage. No sustituye ninguna regla clínica: el backend sigue
// siendo la autoridad; aquí solo se imitan las transiciones de estado del Domain para que la UI sea navegable.
// En modo HTTP (por defecto) solo se usan las funciones sin backend (notas, diagnósticos, medicación, traslados), que
// entonces reciben ids de atenciones REALES: por eso sus registros se guardan por id aunque la atención no sea del mock.

const LATENCY_MS = 350;
const CLINIC: ClinicalTransfer['destinationClinic'] = { id: 'mock-clinic-1', name: 'Hospital DEMO' };

const minutesAgo = (m: number) => new Date(Date.now() - m * 60_000).toISOString();
const newId = () => `mock-${crypto.randomUUID()}`;
const newTemporaryCode = () => `NN-${crypto.randomUUID().replace(/-/g, '').toUpperCase()}`;
// Códigos NN reconocibles como de prueba: `NN-` + 32 hex, igual que TemporaryPatientCode.
const demoTemporaryCode = (n: number) => `NN-${'0'.repeat(30)}D${n}`;

const patients: PatientSummary[] = [
    {
        id: 'mock-patient-1',
        displayName: 'Paciente DEMO Uno',
        birthDate: '1980-01-01',
        biologicalSex: 'Female',
        identityStatus: 'Verified',
        primaryIdentifier: { type: 'InternalCode', value: 'DEMO-0001' },
    },
    {
        id: 'mock-patient-2',
        displayName: 'Paciente DEMO Dos',
        birthDate: '1995-06-15',
        biologicalSex: 'Male',
        identityStatus: 'Unverified',
        primaryIdentifier: { type: 'InternalCode', value: 'DEMO-0002' },
    },
];

const encounters: ClinicalEncounterSummary[] = [
    {
        id: 'mock-encounter-1',
        patientId: 'mock-patient-1',
        patient: patients[0],
        temporaryPatientCode: null,
        clinicId: CLINIC.id,
        type: 'Emergency',
        status: 'InProgress',
        reason: 'Motivo de consulta de prueba',
        startedAtUtc: minutesAgo(90),
        endedAtUtc: null,
        latestTriagePriority: 'Urgent',
    },
    {
        id: 'mock-encounter-2',
        patientId: null,
        patient: null,
        temporaryPatientCode: demoTemporaryCode(1),
        clinicId: CLINIC.id,
        type: 'Emergency',
        status: 'Created',
        reason: null,
        startedAtUtc: minutesAgo(20),
        endedAtUtc: null,
        latestTriagePriority: null,
    },
    {
        id: 'mock-encounter-3',
        patientId: null,
        patient: null,
        temporaryPatientCode: demoTemporaryCode(2),
        clinicId: null,
        type: 'Prehospital',
        status: 'Transferred',
        reason: 'Traslado prehospitalario de prueba',
        startedAtUtc: minutesAgo(40),
        endedAtUtc: null,
        latestTriagePriority: 'VeryUrgent',
    },
];

const triages: Triage[] = [
    {
        id: 'mock-triage-1',
        encounterId: 'mock-encounter-1',
        priority: 'Urgent',
        reason: null,
        observations: 'Observación de prueba',
        performedAtUtc: minutesAgo(80),
    },
];
const vitalSigns: Record<Guid, VitalSign[]> = {};
const notes: Record<Guid, ClinicalNote[]> = {};
const diagnoses: Record<Guid, Diagnosis[]> = {};
const prescriptions: Record<Guid, Prescription[]> = {};
const administrations: Record<Guid, MedicationAdministration[]> = {};

const medications: Medication[] = [
    {
        id: 'mock-med-1',
        code: 'DEMO-PAR',
        name: 'Paracetamol',
        presentation: 'Tableta',
        concentration: '500 mg',
        defaultRoute: 'Oral',
        status: 'Active',
    },
    {
        id: 'mock-med-2',
        code: 'DEMO-IBU',
        name: 'Ibuprofeno',
        presentation: 'Tableta',
        concentration: '400 mg',
        defaultRoute: 'Oral',
        status: 'Active',
    },
    {
        id: 'mock-med-3',
        code: 'DEMO-SSN',
        name: 'Solución salina 0,9 %',
        presentation: 'Bolsa',
        concentration: '1000 mL',
        defaultRoute: 'Intravenous',
        status: 'Active',
    },
    {
        id: 'mock-med-4',
        code: 'DEMO-AMX',
        name: 'Amoxicilina',
        presentation: 'Cápsula',
        concentration: '500 mg',
        defaultRoute: 'Oral',
        status: 'Inactive',
    },
];

const transfers: ClinicalTransfer[] = [
    {
        id: 'mock-transfer-1',
        encounterId: 'mock-encounter-3',
        status: 'InTransit',
        originClinic: null,
        destinationClinic: CLINIC,
        ambulance: { id: 'mock-amb-1', code: 'AMB-DEMO-01', callSign: 'Unidad DEMO 1' },
        requestedAtUtc: minutesAgo(35),
        sentAtUtc: minutesAgo(30),
        receivedAtUtc: null,
        patient: null,
        temporaryPatientCode: demoTemporaryCode(2),
        reason: 'Traslado prehospitalario de prueba',
        latestTriagePriority: 'VeryUrgent',
    },
];

// Transiciones relevantes del Domain (ClinicalEncounter._allowedTransitions).
const CLOSABLE: ClinicalEncounterStatus[] = ['InProgress', 'Received', 'InEvolution'];
const NOT_LISTED: ClinicalEncounterStatus[] = ['Closed', 'Cancelled', 'Transferred'];

function delay<T>(value: T, signal?: AbortSignal): Promise<T> {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => resolve(structuredClone(value)), LATENCY_MS);
        signal?.addEventListener('abort', () => {
            clearTimeout(timer);
            reject(new DOMException('Aborted', 'AbortError'));
        });
    });
}

function findEncounter(id: Guid): ClinicalEncounterSummary {
    const encounter = encounters.find((e) => e.id === id);
    if (!encounter) throw new ClinicalApiError(404, 'Atención no encontrada.');
    return encounter;
}

function ensureOpen(encounter: ClinicalEncounterSummary) {
    if (encounter.status === 'Closed' || encounter.status === 'Cancelled') {
        throw new ClinicalApiError(409, 'La atención está cerrada o cancelada.');
    }
}

// Primer registro clínico: Created → InProgress, igual que haría el backend al empezar la atención.
function markStarted(encounter: ClinicalEncounterSummary) {
    if (encounter.status === 'Created') encounter.status = 'InProgress';
}

function withEncounter<T>(id: Guid, run: (encounter: ClinicalEncounterSummary) => T, signal?: AbortSignal): Promise<T> {
    try {
        return delay(run(findEncounter(id)), signal);
    } catch (error) {
        return Promise.reject(error);
    }
}

// Registros simulados de una atención que puede ser real (modo HTTP): sin ella, no se imitan estado ni transiciones.
function withRecords<T>(id: Guid, run: () => T): Promise<T> {
    try {
        const encounter = encounters.find((e) => e.id === id);
        if (encounter) ensureOpen(encounter);
        const result = run();
        if (encounter) markStarted(encounter);
        return delay(result);
    } catch (error) {
        return Promise.reject(error);
    }
}

const readRecords = <T,>(id: Guid, store: Record<Guid, T[]>, signal?: AbortSignal) => delay(store[id] ?? [], signal);

const toAdmission = (e: ClinicalEncounterSummary): AdmissionEncounterSummary => ({
    encounterId: e.id,
    patientId: e.patientId,
    patientDisplayName: e.patient?.displayName ?? null,
    temporaryPatientCode: e.temporaryPatientCode,
    type: e.type,
    status: e.status,
    startedAtUtc: e.startedAtUtc,
    clinicId: e.clinicId,
});

const maskIdentifier = (value: string) => `***${value.replace(/[^0-9a-z]/gi, '').slice(-4)}`;

export const mockClinicalApi: ClinicalApi = {
    getClinicalContext(signal) {
        return delay({ staffId: 'mock-staff-1', clinics: [CLINIC] }, signal);
    },

    // Huella: solo existe con backend real (ADR-010). En simulación no hay lectores y no se registra nada.
    getPatient(patientId, signal) {
        const patient = patients.find((p) => p.id === patientId);
        return patient ? delay(patient, signal) : Promise.reject(new ClinicalApiError(404, 'El paciente no existe.'));
    },

    listFingerprintEnrollers(signal) {
        return delay([], signal);
    },

    listPatientFingerprints(_patientId, signal) {
        return delay([], signal);
    },

    async openFingerprintEnrollment() {
        throw new ClinicalApiError(409, 'El registro de huella necesita el backend real.');
    },

    async getFingerprintEnrollment() {
        throw new ClinicalApiError(404, 'El registro de huella necesita el backend real.');
    },

    async cancelFingerprintEnrollment() {
        throw new ClinicalApiError(404, 'El registro de huella necesita el backend real.');
    },

    registerPatient(request) {
        const patient: PatientSummary = {
            id: newId(),
            displayName: `${request.givenNames.trim()} ${request.familyNames.trim()}`,
            birthDate: request.dateOfBirth,
            biologicalSex: request.sex,
            identityStatus: 'Unverified',
            primaryIdentifier: request.identifier ? { type: request.identifier.type, value: maskIdentifier(request.identifier.value) } : null,
        };
        patients.push(patient);
        return delay(patient);
    },

    identifyEncounterPatient(encounterId, patientId) {
        return withEncounter(encounterId, (encounter) => {
            const patient = patients.find((p) => p.id === patientId);
            if (!patient) throw new ClinicalApiError(404, 'Paciente no encontrado.');
            if (encounter.patientId) throw new ClinicalApiError(409, 'La atención ya fue identificada.');
            encounter.patientId = patient.id;
            encounter.patient = patient;
            encounter.identifiedAtUtc = new Date().toISOString();
        });
    },

    listAdmissionEncounters(options, signal) {
        return delay(
            encounters.filter((e) => !NOT_LISTED.includes(e.status) && (!options?.unidentifiedOnly || !e.patientId)).map(toAdmission),
            signal,
        );
    },

    createVitalSign(encounterId, request) {
        return withEncounter(encounterId, (encounter) => {
            ensureOpen(encounter);
            const vital: VitalSign = { ...request, id: newId(), triageId: null, unit: vitalSignMeta(request.type).unit };
            (vitalSigns[encounterId] ??= []).unshift(vital);
            markStarted(encounter);
            return vital;
        });
    },

    searchPatients({ value }, signal) {
        const term = value.trim().toLowerCase();
        const matches = patients.filter(
            (p) => p.displayName.toLowerCase().includes(term) || p.primaryIdentifier?.value.toLowerCase() === term,
        );
        return delay(matches, signal);
    },

    listActiveEncounters(signal) {
        return delay(
            encounters.filter((e) => !NOT_LISTED.includes(e.status)),
            signal,
        );
    },

    getEncounter(encounterId, signal) {
        return withEncounter(encounterId, (e) => e, signal);
    },

    createEncounter({ type, patientId, reason }) {
        const patient = patientId ? patients.find((p) => p.id === patientId) : null;
        if (patientId && !patient) return Promise.reject(new ClinicalApiError(404, 'Paciente no encontrado.'));
        if (!patient && type !== 'Emergency' && type !== 'Prehospital') {
            return Promise.reject(new ClinicalApiError(409, 'Solo las atenciones de emergencia admiten paciente NN.'));
        }
        const encounter: ClinicalEncounterSummary = {
            id: newId(),
            patientId: patient?.id ?? null,
            patient: patient ?? null,
            temporaryPatientCode: patient ? null : newTemporaryCode(),
            clinicId: CLINIC.id,
            type,
            status: 'Created',
            reason,
            startedAtUtc: new Date().toISOString(),
            endedAtUtc: null,
            latestTriagePriority: null,
        };
        encounters.unshift(encounter);
        return delay(encounter);
    },

    closeEncounter(encounterId, { dischargeNote }) {
        return withEncounter(encounterId, (encounter) => {
            if (!CLOSABLE.includes(encounter.status)) {
                throw new ClinicalApiError(409, `Una atención en estado ${encounter.status} no puede cerrarse.`);
            }
            const now = new Date().toISOString();
            if (dischargeNote) {
                (notes[encounterId] ??= []).unshift({
                    id: newId(),
                    type: 'Discharge',
                    content: dischargeNote,
                    occurredAtUtc: now,
                    createdAtUtc: now,
                    authorDisplayName: null,
                });
            }
            encounter.status = 'Closed';
            encounter.endedAtUtc = now;
        });
    },

    listTriages(encounterId, signal) {
        return withEncounter(encounterId, () => triages.filter((t) => t.encounterId === encounterId), signal);
    },

    createTriage(encounterId, request) {
        return withEncounter(encounterId, (encounter) => {
            ensureOpen(encounter);
            const now = new Date().toISOString();
            const triage: Triage = {
                id: newId(),
                encounterId,
                priority: request.priority,
                reason: request.reason,
                observations: request.observations,
                performedAtUtc: request.performedAtUtc ?? now,
            };
            triages.unshift(triage);
            (vitalSigns[encounterId] ??= []).unshift(
                ...request.measurements.map((v) => ({ ...v, id: newId(), triageId: triage.id, unit: vitalSignMeta(v.type).unit })),
            );
            encounter.latestTriagePriority = request.priority;
            markStarted(encounter);
            return triage;
        });
    },

    listVitalSigns(encounterId, signal) {
        return withEncounter(encounterId, () => vitalSigns[encounterId] ?? [], signal);
    },

    listNotes(encounterId, signal) {
        return readRecords(encounterId, notes, signal);
    },

    createNote(encounterId, request) {
        return withRecords(encounterId, () => {
            const note: ClinicalNote = { ...request, id: newId(), createdAtUtc: new Date().toISOString(), authorDisplayName: null };
            (notes[encounterId] ??= []).unshift(note);
            return note;
        });
    },

    listDiagnoses(encounterId, signal) {
        return readRecords(encounterId, diagnoses, signal);
    },

    createDiagnosis(encounterId, request) {
        return withRecords(encounterId, () => {
            const diagnosis: Diagnosis = { ...request, id: newId(), recordedAtUtc: new Date().toISOString() };
            (diagnoses[encounterId] ??= []).unshift(diagnosis);
            return diagnosis;
        });
    },

    listMedications(signal) {
        return delay(medications, signal);
    },

    listPrescriptions(encounterId, signal) {
        return readRecords(encounterId, prescriptions, signal);
    },

    createPrescription(encounterId, request) {
        return withRecords(encounterId, () => {
            const details = request.details.map((d) => {
                const medication = medications.find((m) => m.id === d.medicationId);
                if (!medication || medication.status !== 'Active') {
                    throw new ClinicalApiError(409, 'El medicamento no está activo en el catálogo.');
                }
                return { ...d, id: newId(), medicationName: medication.name };
            });
            const now = new Date().toISOString();
            const prescription: Prescription = {
                id: newId(),
                encounterId,
                status: 'Issued',
                notes: request.notes,
                createdAtUtc: now,
                issuedAtUtc: now,
                details,
            };
            (prescriptions[encounterId] ??= []).unshift(prescription);
            return prescription;
        });
    },

    listMedicationAdministrations(encounterId, signal) {
        return readRecords(encounterId, administrations, signal);
    },

    createMedicationAdministration(encounterId, request) {
        return withRecords(encounterId, () => {
            const medication = medications.find((m) => m.id === request.medicationId);
            if (!medication || medication.status !== 'Active') {
                throw new ClinicalApiError(409, 'El medicamento no está activo en el catálogo.');
            }
            const administration: MedicationAdministration = {
                id: newId(),
                encounterId,
                medicationId: medication.id,
                medicationName: medication.name,
                prescriptionId: request.prescriptionId,
                prescriptionDetailId: request.prescriptionDetailId,
                dose: request.dose,
                doseUnit: request.doseUnit,
                route: request.route,
                administeredAtUtc: request.administeredAtUtc,
                status: 'Completed',
                notes: request.notes,
            };
            (administrations[encounterId] ??= []).unshift(administration);
            return administration;
        });
    },

    listIncomingTransfers(signal) {
        return delay(
            transfers.filter((t) => t.status === 'Requested' || t.status === 'InTransit'),
            signal,
        );
    },

    receiveTransfer(transferId) {
        const transfer = transfers.find((t) => t.id === transferId);
        if (!transfer) return Promise.reject(new ClinicalApiError(404, 'Traslado no encontrado.'));
        if (transfer.status !== 'InTransit') {
            return Promise.reject(new ClinicalApiError(409, `Un traslado en estado ${transfer.status} no puede recibirse.`));
        }
        transfer.status = 'Received';
        transfer.receivedAtUtc = new Date().toISOString();
        const encounter = encounters.find((e) => e.id === transfer.encounterId);
        if (encounter && encounter.status === 'Transferred') {
            encounter.status = 'Received';
            encounter.clinicId = transfer.destinationClinic.id;
        }
        return delay(undefined);
    },
};
