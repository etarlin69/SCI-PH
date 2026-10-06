// MOCK / SIMULACIÓN: los endpoints clínicos del Web BFF aún no existen (docs/HOSPITAL_CONTRATOS.md). Mientras sea
// true, la UI muestra "Simulación" en la cabecera y en cada confirmación. Cuando el backend publique los contratos,
// basta con crear `httpClinicalApi: ClinicalApi` sobre `apiRequest` (src/api) y asignarlo en api/index.ts; las pantallas no
// cambian. No se hace fetch a rutas inexistentes.
export const CLINICAL_API_IS_MOCK = true;
