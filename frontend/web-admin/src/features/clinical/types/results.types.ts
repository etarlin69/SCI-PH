export type ClinicalResultType = 'Laboratory' | 'Imaging' | 'PointOfCare' | 'Procedure' | 'Other';
export type ClinicalResultStatus = 'Pending' | 'Final' | 'Corrected' | 'Cancelled';

/** Resultado de la atención (GET /encounters/{id}/results): órdenes pendientes y valores registrados. */
export interface ClinicalResult {
    id: string;
    encounterId: string;
    type: ClinicalResultType;
    status: ClinicalResultStatus;
    name: string;
    codeSystem: string | null;
    code: string | null;
    resultText: string | null;
    numericValue: number | null;
    unit: string | null;
    referenceRange: string | null;
    resultedAtUtc: string | null;
    recordedByDisplayName: string | null;
}

export const RESULT_TYPE_LABELS: Record<ClinicalResultType, string> = {
    Laboratory: 'Laboratorio',
    Imaging: 'Imagen',
    PointOfCare: 'Punto de atención',
    Procedure: 'Procedimiento',
    Other: 'Otro',
};

export const RESULT_STATUS_LABELS: Record<ClinicalResultStatus, string> = {
    Pending: 'Pendiente',
    Final: 'Final',
    Corrected: 'Corregido',
    Cancelled: 'Cancelado',
};

/** Valor numérico con unidad o el texto del resultado; null si aún no tiene valor. */
export function resultValue(result: Pick<ClinicalResult, 'numericValue' | 'unit' | 'resultText'>): string | null {
    if (result.numericValue !== null) return result.unit ? `${result.numericValue} ${result.unit}` : `${result.numericValue}`;
    return result.resultText;
}
