import type { BadgeTone } from '../../../components/shared';
import type {
    BiologicalSex,
    ClinicalEncounterStatus,
    ClinicalEncounterSummary,
    ClinicalEncounterType,
    ClinicalNoteType,
    ClinicalTransferStatus,
    DateOnly,
    DiagnosisType,
    MedicationRoute,
    PatientIdentifierType,
    TriagePriority,
    UtcDateTime,
    VitalSignType,
} from '../types/clinical.types';

// Etiquetas en español de los enums del Domain. Las claves son los nombres reales de los miembros C#.

export const ENCOUNTER_STATUS: Record<ClinicalEncounterStatus, { label: string; tone: BadgeTone }> = {
    Created: { label: 'Creada', tone: 'neutral' },
    InProgress: { label: 'En curso', tone: 'info' },
    Transferred: { label: 'En traslado', tone: 'accent' },
    Received: { label: 'Recibida', tone: 'info' },
    InEvolution: { label: 'En evolución', tone: 'warning' },
    Closed: { label: 'Cerrada', tone: 'success' },
    Cancelled: { label: 'Cancelada', tone: 'neutral' },
};

export const ENCOUNTER_TYPE_LABELS: Record<ClinicalEncounterType, string> = {
    Consultation: 'Consulta',
    Emergency: 'Emergencia',
    Prehospital: 'Prehospitalaria',
    Hospitalization: 'Hospitalización',
};

/** Estados desde los que el Domain permite pasar a Closed. Solo orienta la UI; el backend decide. */
export const CLOSABLE_STATUSES: ClinicalEncounterStatus[] = ['InProgress', 'Received', 'InEvolution'];
export const isEncounterOpen = (status: ClinicalEncounterStatus) => status !== 'Closed' && status !== 'Cancelled';
/** Estados que admiten registros clínicos (EncounterRecordService.RecordableStatuses). Solo orienta la UI; el backend decide. */
export const RECORDABLE_STATUSES: ClinicalEncounterStatus[] = ['Created', 'InProgress', 'Received', 'InEvolution'];
export const acceptsClinicalRecords = (status: ClinicalEncounterStatus) => RECORDABLE_STATUSES.includes(status);
/**
 * La atención aún no empezó en esta clínica (EncounterRecordService.CarePendingStatuses): el primer registro clínico la
 * pasa a InProgress. La UI recarga entonces la atención para mostrar el estado nuevo.
 */
export const startsCare = (status: ClinicalEncounterStatus) => status === 'Created' || status === 'Received';
/** Estados desde los que el Domain permite trasladar (→ Transferred). Solo orienta la UI; el backend decide (409). */
export const TRANSFERABLE_STATUSES: ClinicalEncounterStatus[] = ['InProgress', 'InEvolution'];
export const canBeTransferred = (status: ClinicalEncounterStatus) => TRANSFERABLE_STATUSES.includes(status);

/** Escala de cinco niveles. El color es solo visual: al backend se envía la clave del enum. */
export const TRIAGE_PRIORITIES: { value: TriagePriority; label: string; color: string; className: string }[] = [
    { value: 'Immediate', label: 'Inmediata', color: 'Rojo', className: 'bg-red-100 text-red-800 ring-red-300' },
    { value: 'VeryUrgent', label: 'Muy urgente', color: 'Naranja', className: 'bg-orange-100 text-orange-800 ring-orange-300' },
    { value: 'Urgent', label: 'Urgente', color: 'Amarillo', className: 'bg-yellow-100 text-yellow-900 ring-yellow-300' },
    { value: 'Standard', label: 'Normal', color: 'Verde', className: 'bg-green-100 text-green-800 ring-green-300' },
    { value: 'NonUrgent', label: 'No urgente', color: 'Azul', className: 'bg-sky-100 text-sky-800 ring-sky-300' },
];

export const triagePriorityMeta = (priority: TriagePriority) => TRIAGE_PRIORITIES.find((p) => p.value === priority)!;

/** Unidades UCUM y límites de plausibilidad copiados de VitalSignLimits (Domain): validan errores de tecleo, no umbrales clínicos. */
export const VITAL_SIGNS: { type: VitalSignType; label: string; unit: string; unitLabel: string; min: number; max: number; step: number }[] = [
    { type: 'SystolicBloodPressure', label: 'Presión sistólica', unit: 'mm[Hg]', unitLabel: 'mmHg', min: 0, max: 300, step: 1 },
    { type: 'DiastolicBloodPressure', label: 'Presión diastólica', unit: 'mm[Hg]', unitLabel: 'mmHg', min: 0, max: 250, step: 1 },
    { type: 'HeartRate', label: 'Frecuencia cardíaca', unit: '/min', unitLabel: 'lpm', min: 0, max: 300, step: 1 },
    { type: 'RespiratoryRate', label: 'Frecuencia respiratoria', unit: '/min', unitLabel: 'rpm', min: 0, max: 100, step: 1 },
    { type: 'OxygenSaturation', label: 'Saturación de O₂', unit: '%', unitLabel: '%', min: 0, max: 100, step: 1 },
    { type: 'Temperature', label: 'Temperatura', unit: 'Cel', unitLabel: '°C', min: 25, max: 45, step: 0.1 },
    { type: 'BloodGlucose', label: 'Glucemia', unit: 'mg/dL', unitLabel: 'mg/dL', min: 0, max: 2000, step: 1 },
    { type: 'PainScore', label: 'Dolor (EVA)', unit: '{score}', unitLabel: '0–10', min: 0, max: 10, step: 1 },
    { type: 'GlasgowComaScale', label: 'Glasgow', unit: '{score}', unitLabel: '3–15', min: 3, max: 15, step: 1 },
    { type: 'Weight', label: 'Peso', unit: 'kg', unitLabel: 'kg', min: 0.2, max: 500, step: 0.1 },
    { type: 'Height', label: 'Talla', unit: 'cm', unitLabel: 'cm', min: 20, max: 280, step: 1 },
];

export const vitalSignMeta = (type: VitalSignType) => VITAL_SIGNS.find((v) => v.type === type)!;

export const NOTE_TYPE_LABELS: Record<ClinicalNoteType, string> = {
    Assessment: 'Valoración',
    Evolution: 'Evolución',
    Prehospital: 'Prehospitalaria',
    Discharge: 'Egreso',
    TransferSummary: 'Resumen de traslado',
    Other: 'Otra',
};

export const DIAGNOSIS_TYPE_LABELS: Record<DiagnosisType, string> = {
    Presumptive: 'Presuntivo',
    Definitive: 'Definitivo',
};

export const ROUTE_LABELS: Record<MedicationRoute, string> = {
    Oral: 'Oral',
    Intravenous: 'Intravenosa (IV)',
    Intramuscular: 'Intramuscular (IM)',
    Subcutaneous: 'Subcutánea (SC)',
    Inhalation: 'Inhalatoria',
    Topical: 'Tópica',
    Sublingual: 'Sublingual',
    Rectal: 'Rectal',
    Nasal: 'Nasal',
    Ophthalmic: 'Oftálmica',
    Otic: 'Ótica',
    Transdermal: 'Transdérmica',
    Other: 'Otra',
};

export const TRANSFER_STATUS: Record<ClinicalTransferStatus, { label: string; tone: BadgeTone }> = {
    Requested: { label: 'Solicitado', tone: 'neutral' },
    InTransit: { label: 'En camino', tone: 'warning' },
    Received: { label: 'Recibido', tone: 'success' },
    Completed: { label: 'Completado', tone: 'success' },
    Cancelled: { label: 'Cancelado', tone: 'neutral' },
};

export const SEX_LABELS: Record<BiologicalSex, string> = {
    Female: 'Femenino',
    Male: 'Masculino',
    Intersex: 'Intersexual',
    Unknown: 'No determinado',
};

export const IDENTIFIER_TYPE_LABELS: Record<PatientIdentifierType, string> = {
    NationalId: 'Cédula',
    Passport: 'Pasaporte',
    QrCode: 'QR SCI-PH',
    InternalCode: 'Código interno',
    Phone: 'Teléfono',
    Email: 'Correo',
    Other: 'Otro',
};

/** Edad en años cumplidos derivada de la fecha de nacimiento (no se persiste). */
export function ageFromBirthDate(birthDate: DateOnly | null, now = new Date()): number | null {
    if (!birthDate) return null;
    const [y, m, d] = birthDate.split('-').map(Number);
    let age = now.getFullYear() - y;
    if (now.getMonth() + 1 < m || (now.getMonth() + 1 === m && now.getDate() < d)) age--;
    return age;
}

/** Nombre a mostrar del sujeto de una atención: paciente identificado o NN con su código temporal. */
export function encounterSubject(encounter: Pick<ClinicalEncounterSummary, 'patient'>): string {
    return encounter.patient ? encounter.patient.displayName : 'Paciente NN (no identificado)';
}

/** Código NN abreviado para listas; el completo se muestra en el detalle. */
export const shortTemporaryCode = (code: string) => `${code.slice(0, 7)}…${code.slice(-4)}`;

export function formatDateTimeUtc(value: UtcDateTime): string {
    return new Intl.DateTimeFormat('es-DO', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value));
}

export function nowForDateTimeInput(): string {
    const d = new Date();
    d.setSeconds(0, 0);
    return new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

/** Convierte el valor de un <input type="datetime-local"> (hora local) a UTC ISO. */
export const localInputToUtc = (value: string): UtcDateTime => new Date(value).toISOString();
