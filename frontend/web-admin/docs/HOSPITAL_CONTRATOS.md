# Web Hospital (Rol 4) — contratos que usa el frontend

Estado: **vertical clínica 1 integrada con el Web BFF real** (pacientes, atenciones, admisión/NN, triage y signos
vitales). Lo demás sigue en el adaptador MOCK y la UI lo marca con «Simulación» en cada sección afectada.

- Puerto: `ClinicalApi` (`src/features/clinical/api/clinicalApi.ts`).
- Adaptador real: `httpClinicalApi.ts` (`apiRequest` de `src/api`: Bearer en memoria, refresh single-flight, ProblemDetails).
- Adaptador simulado: `mockClinicalApi.ts` (datos DEMO en memoria). `VITE_CLINICAL_API_MODE=mock` simula todo el módulo.
- Funciones reales vs. simuladas: `src/features/clinical/api/mode.ts` (`isSimulated(feature)`).
- Contrato del backend (fuente de verdad): [`docs/api/web-bff-contracts.md`](../../../docs/api/web-bff-contracts.md).

## Reglas comunes

- Prefijo `/api/web`, autenticación compartida (`src/auth`). El módulo clínico no maneja tokens.
- Errores como ProblemDetails. La UI distingue 400/401/403/404/409/5xx y traduce los `detail` clínicos conocidos
  (`describeError`); un error nunca se muestra como «sin datos».
- **Autoría desde la sesión:** el cliente no envía `performedBy`, `measuredBy` ni `identifiedBy`.
- **Enums como texto** con el nombre del miembro C# (`"Immediate"`, `"InProgress"`). El BFF rechaza números y campos
  desconocidos: por eso `VitalSignInput` **no lleva `unit`** (la fija el Domain y llega en la respuesta).
- **Identificadores fuera de la URL:** la búsqueda de pacientes es `POST` con cuerpo; la respuesta los enmascara.
- **Clínica activa:** `GET /api/web/clinical/context` al entrar. Con una clínica no se envía `clinicId`; con varias, el
  selector de clínica (`ClinicSelector`) elige y se envía en listas y creación. El backend lo valida siempre.
- **Hora de las mediciones:** si el usuario no cambia la hora del formulario se envía la hora exacta del envío (el campo
  tiene precisión de minutos y podría quedar antes del inicio de una atención recién abierta).

## Permisos por pantalla (permisos reales de Permissions.cs)

| Pantalla | Acceso (cualquiera) | Acciones dentro |
|---|---|---|
| Recepción | `Encounters.Create`, `Patients.Read` | Buscar: `Patients.Read` · registrar: `Patients.Create` · abrir atención: `Encounters.Create` · admisión e identificar NN: `Patients.Identify` · traslados: `Transfers.Read` (simulado) |
| Pacientes | `Patients.Read` | Registrar: `Patients.Create` |
| Atenciones / Expediente | `Encounters.Read` | Diagnóstico: `Diagnoses.Write` · nota: `ClinicalNotes.Write` (ambos simulados) |
| Triage | `Triage.Write` | Signos vitales en el triage o sueltos: `VitalSigns.Write` |
| Medicación y egreso | `Medications.Read`, `Prescriptions.Write`, `MedicationAdministrations.Write` | Simulado; el egreso real está deshabilitado hasta tener backend |
| Transferencias | `Transfers.Read` | Simulado |

Recepción **no** tiene `Encounters.Read`: tras crear una atención se queda en Recepción con el resumen devuelto por el
`POST` y la ve en «Atenciones en admisión» (proyección sin datos clínicos).

## Endpoints reales (vertical clínica 1)

| Área | Método | Ruta | Pantalla |
|---|---|---|---|
| Contexto | GET | `/api/web/clinical/context` | Selector de clínica |
| Patients | POST | `/api/web/patients` | Recepción, Pacientes |
| Patients | POST | `/api/web/patients/search` | Recepción, Pacientes |
| Encounters | GET | `/api/web/encounters?clinicId=` | Atenciones, Triage |
| Encounters | GET | `/api/web/encounters/{id}` | Expediente, Triage |
| Encounters | POST | `/api/web/encounters` | Recepción |
| Encounters | POST | `/api/web/encounters/{id}/identify` | Recepción |
| Admissions | GET | `/api/web/admissions/encounters?clinicId=&unidentified=` | Recepción |
| Triage | GET · POST | `/api/web/encounters/{id}/triages` | Triage |
| VitalSigns | GET · POST | `/api/web/encounters/{id}/vital-signs` | Triage |

## Aún simulado (MOCK) — pendiente de backend

| Función del puerto | Ruta prevista |
|---|---|
| `listNotes` / `createNote` | `/api/web/encounters/{id}/notes` |
| `listDiagnoses` / `createDiagnosis` | `/api/web/encounters/{id}/diagnoses` |
| `listMedications` | `/api/web/medications` |
| `listPrescriptions` / `createPrescription` | `/api/web/encounters/{id}/prescriptions` |
| `listMedicationAdministrations` | `/api/web/encounters/{id}/medication-administrations` |
| `listIncomingTransfers` / `receiveTransfer` | `/api/web/transfers`, `/api/web/transfers/{id}/receive` |
| `closeEncounter` | `/api/web/encounters/{id}/close` — en modo HTTP se rechaza (no se simula sobre atenciones reales) |

Con atenciones reales, las notas, diagnósticos y prescripciones simuladas se guardan en memoria por id de atención y
se pierden al recargar.
