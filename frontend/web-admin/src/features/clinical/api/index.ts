import type { ClinicalApi } from './clinicalApi';
import { mockClinicalApi } from './mockClinicalApi';

export * from './clinicalApi';

export { CLINICAL_API_IS_MOCK } from './mode';

export const clinicalApi: ClinicalApi = mockClinicalApi;
