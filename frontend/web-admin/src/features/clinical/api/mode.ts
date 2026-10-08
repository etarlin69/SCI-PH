// Modo del puerto clínico. Por defecto (`http`) las funciones con backend real van al Web BFF y el resto sigue en el
// adaptador MOCK; `VITE_CLINICAL_API_MODE=mock` simula todo el módulo (desarrollo visual sin backend).
// Solo constantes: Layout importa este módulo sin cargar el adaptador mock en el chunk principal.

export type ClinicalFeature =
    | 'patients'
    | 'encounters'
    | 'admissions'
    | 'triage'
    | 'vitalSigns'
    | 'notes'
    | 'diagnoses'
    | 'medications'
    | 'prescriptions'
    | 'administrations'
    | 'discharge'
    | 'transfers';

/** Funciones con endpoints reales en el Web BFF (verticales clínicas 1 y 2). */
export const HTTP_CLINICAL_FEATURES: readonly ClinicalFeature[] = [
    'patients',
    'encounters',
    'admissions',
    'triage',
    'vitalSigns',
    'notes',
    'diagnoses',
    'discharge',
];

/** true = todo el módulo hospitalario es simulado. */
export const CLINICAL_API_IS_MOCK = import.meta.env.VITE_CLINICAL_API_MODE === 'mock';

/** Una función es simulada si el módulo entero lo es o si aún no tiene backend. La UI lo indica con «Simulación». */
export const isSimulated = (feature: ClinicalFeature): boolean => CLINICAL_API_IS_MOCK || !HTTP_CLINICAL_FEATURES.includes(feature);
