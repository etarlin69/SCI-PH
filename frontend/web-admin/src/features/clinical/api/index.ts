import type { ClinicalApi } from './clinicalApi';
import { mockClinicalApi } from './mockClinicalApi';

export * from './clinicalApi';

// MOCK: los endpoints clínicos del Web BFF aún no existen (docs/HOSPITAL_CONTRATOS.md). Mientras sea true, la UI
// muestra "Simulación" en la cabecera y en cada confirmación. Al integrar el backend se sustituirá por el adaptador
// HTTP construido sobre el cliente compartido del Rol 5; no se hace fetch a rutas inexistentes.
export const CLINICAL_API_IS_MOCK = true;

export const clinicalApi: ClinicalApi = mockClinicalApi;
