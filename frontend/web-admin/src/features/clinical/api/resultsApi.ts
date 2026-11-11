import { apiRequest } from '../../../api';
import type { ClinicalResult, ClinicalResultType } from '../types/results.types';

// Resultados de la atención (Web BFF real): pedir, registrar el valor, corregir y cancelar. Rutas: docs/HOSPITAL_CONTRATOS.md.
const BASE = '/api/web/encounters';
const id = (value: string) => encodeURIComponent(value);

export interface RecordResultValue {
    resultText: string | null;
    numericValue: number | null;
    unit: string | null;
}

export const resultsApi = {
    list: (encounterId: string, signal?: AbortSignal) => apiRequest<ClinicalResult[]>(`${BASE}/${id(encounterId)}/results`, { signal }),

    request: (encounterId: string, body: { type: ClinicalResultType; name: string; referenceRange: string | null }) =>
        apiRequest<ClinicalResult>(`${BASE}/${id(encounterId)}/results`, { method: 'POST', body }),

    recordFinal: (encounterId: string, resultId: string, body: RecordResultValue) =>
        apiRequest<ClinicalResult>(`${BASE}/${id(encounterId)}/results/${id(resultId)}/final`, { method: 'POST', body }),

    correct: (encounterId: string, resultId: string, body: RecordResultValue) =>
        apiRequest<ClinicalResult>(`${BASE}/${id(encounterId)}/results/${id(resultId)}/correct`, { method: 'POST', body }),

    cancel: (encounterId: string, resultId: string) =>
        apiRequest<ClinicalResult>(`${BASE}/${id(encounterId)}/results/${id(resultId)}/cancel`, { method: 'POST' }),
};
