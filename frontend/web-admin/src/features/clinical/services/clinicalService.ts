import type { 
  AmbuTransfer, 
  ClinicalEncounter, 
  Patient, 
  VitalSigns,
  DiagnosticEntry,
  ProcedureEntry,
  PrescriptionEntry,
  ClinicalNote
} from '../types/clinical.types';

const API_BASE_URL = '/api/v1';

const getAuthHeaders = () => {
  const token = localStorage.getItem('token') || '';
  return {
    'Content-Type': 'application/json',
    Authorization: `Bearer ${token}`,
  };
};

export const clinicalService = {
  async getAmbuTransfers(): Promise<AmbuTransfer[]> {
    const res = await fetch(`${API_BASE_URL}/transfers/incoming`, { headers: getAuthHeaders() });
    if (!res.ok) return [];
    return res.json();
  },

  async searchPatient(query: string): Promise<Patient | null> {
    if (!query) return null;
    const res = await fetch(`${API_BASE_URL}/patients/search?q=${encodeURIComponent(query)}`, { headers: getAuthHeaders() });
    if (!res.ok) return null;
    return res.json();
  },

  async getEncounters(): Promise<ClinicalEncounter[]> {
    const res = await fetch(`${API_BASE_URL}/encounters/active`, { headers: getAuthHeaders() });
    if (!res.ok) return [];
    return res.json();
  },

  async saveTriage(patientId: string, vitals: VitalSigns, priority: string): Promise<boolean> {
    const res = await fetch(`${API_BASE_URL}/triage`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: JSON.stringify({ patientId, vitals, priority }),
    });
    return res.ok;
  },

  async addDiagnostic(encounterId: string, diag: Omit<DiagnosticEntry, 'id'>): Promise<boolean> {
    const res = await fetch(`${API_BASE_URL}/encounters/${encounterId}/diagnostics`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: JSON.stringify(diag),
    });
    return res.ok;
  },

  async addProcedure(encounterId: string, proc: Omit<ProcedureEntry, 'id'>): Promise<boolean> {
    const res = await fetch(`${API_BASE_URL}/encounters/${encounterId}/procedures`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: JSON.stringify(proc),
    });
    return res.ok;
  },

  async addNote(encounterId: string, note: Omit<ClinicalNote, 'id' | 'date'>): Promise<boolean> {
    const res = await fetch(`${API_BASE_URL}/encounters/${encounterId}/notes`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: JSON.stringify(note),
    });
    return res.ok;
  },

  async addPrescription(encounterId: string, prescription: Omit<PrescriptionEntry, 'id' | 'administered'>): Promise<boolean> {
    const res = await fetch(`${API_BASE_URL}/encounters/${encounterId}/prescriptions`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: JSON.stringify(prescription),
    });
    return res.ok;
  },

  async markMedicationAdministered(encounterId: string, prescriptionId: string): Promise<boolean> {
    const res = await fetch(`${API_BASE_URL}/encounters/${encounterId}/prescriptions/${prescriptionId}/administer`, {
      method: 'POST',
      headers: getAuthHeaders(),
    });
    return res.ok;
  },

  async dischargePatient(encounterId: string, summary: string): Promise<boolean> {
    const res = await fetch(`${API_BASE_URL}/encounters/${encounterId}/discharge`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: JSON.stringify({ summary }),
    });
    return res.ok;
  }
};