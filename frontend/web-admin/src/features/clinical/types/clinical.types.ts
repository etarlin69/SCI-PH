export type TriagePriority = 'red' | 'orange' | 'yellow' | 'green' | 'blue';
export type EncounterStatus = 'triage' | 'active' | 'discharged' | 'transferred';
export type DiagnosticType = 'presumptive' | 'definitive';

export interface VitalSigns {
  bloodPressure: string;
  heartRate: number;
  oxygenSaturation: number;
  temperature: number;
}

export interface Patient {
  id: string;
  nationalId: string;
  fullName: string;
  age: number;
  gender: string;
  allergies: string[];
  medicalHistory: string[];
}

export interface AmbuTransfer {
  id: string;
  ambulanceCode: string;
  patientName: string;
  priority: TriagePriority;
  etaMinutes: number;
  conditionSummary: string;
}

export interface DiagnosticEntry {
  id: string;
  code: string; // CIE-10 / SNOMED
  description: string;
  type: DiagnosticType;
}

export interface ProcedureEntry {
  id: string;
  code: string;
  description: string;
  performedAt: string;
  performedBy: string;
}

export interface PrescriptionEntry {
  id: string;
  medication: string;
  dosage: string;
  route: string;
  frequency: string;
  administered: boolean;
  administeredAt?: string;
}

export interface ClinicalNote {
  id: string;
  date: string;
  author: string;
  subjective: string;
  objective: string;
  assessment: string;
  plan: string;
}

export interface ClinicalEncounter {
  id: string;
  patient: Patient;
  vitalSigns: VitalSigns;
  triagePriority: TriagePriority;
  status: EncounterStatus;
  assignedBed: string;
  entryDate: string;
  diagnoses: DiagnosticEntry[];
  procedures: ProcedureEntry[];
  prescriptions: PrescriptionEntry[];
  clinicalNotes: ClinicalNote[];
}