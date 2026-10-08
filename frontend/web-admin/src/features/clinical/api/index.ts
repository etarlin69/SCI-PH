import type { ClinicalApi } from './clinicalApi';
import { httpClinicalApi } from './httpClinicalApi';
import { CLINICAL_API_IS_MOCK } from './mode';
import { mockClinicalApi } from './mockClinicalApi';

export * from './clinicalApi';

export { CLINICAL_API_IS_MOCK, isSimulated, type ClinicalFeature } from './mode';

/** HTTP real por defecto (lo que aún no tiene backend delega en el mock); todo simulado con VITE_CLINICAL_API_MODE=mock. */
export const clinicalApi: ClinicalApi = CLINICAL_API_IS_MOCK ? mockClinicalApi : httpClinicalApi;
