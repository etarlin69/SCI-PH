# Web Hospital (Rol 4) — contratos que usa el frontend

Estado: **verticales clínicas 1, 2 y 3 integradas con el Web BFF real** (pacientes, atenciones, admisión/NN, triage,
signos vitales, notas clínicas, diagnósticos, cierre de la atención, catálogo de medicamentos, prescripciones y Kardex).
Solo los traslados siguen en el adaptador MOCK y la UI lo
marca con «Simulación» en cada sección afectada.

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
| Atenciones / Expediente | `Encounters.Read` | Diagnóstico: `Diagnoses.Write` · nota: `ClinicalNotes.Write` |
| Triage | `Triage.Write` | Signos vitales en el triage o sueltos: `VitalSigns.Write` |
| Medicación y egreso | `Medications.Read`, `Prescriptions.Write`, `MedicationAdministrations.Write` | Emitir prescripción: `Prescriptions.Write` · registrar administración: `MedicationAdministrations.Write` · cerrar la atención: `Encounters.Close`, solo desde `InProgress`/`Received`/`InEvolution`. Ninguna escritura en una atención cerrada |
| Transferencias | `Transfers.Read` | Simulado |

Recepción **no** tiene `Encounters.Read`: tras crear una atención se queda en Recepción con el resumen devuelto por el
`POST` y la ve en «Atenciones en admisión» (proyección sin datos clínicos).

## Endpoints reales (verticales clínicas 1, 2 y 3)

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
| ClinicalNotes | GET · POST | `/api/web/encounters/{id}/notes` | Expediente |
| Diagnoses | GET · POST | `/api/web/encounters/{id}/diagnoses` | Expediente |
| Encounters | POST | `/api/web/encounters/{id}/close` | Medicación y egreso |
| Medications | GET | `/api/web/medications` | Medicación y egreso |
| Prescriptions | GET · POST | `/api/web/encounters/{id}/prescriptions` | Medicación y egreso |
| MedicationAdministrations | GET · POST | `/api/web/encounters/{id}/medication-administrations` | Medicación y egreso (Kardex) |

El **Kardex** no es un recurso propio: la pantalla lo compone con las prescripciones emitidas (con sus líneas) y las
administraciones registradas. Una administración puede vincularse a una línea prescrita (el medicamento debe ser el
prescrito) o registrarse sin prescripción. Prescriptor y responsable de la administración los fija el backend.

## Aún simulado (MOCK) — pendiente de backend

| Función del puerto | Ruta prevista |
|---|---|
| `listIncomingTransfers` / `receiveTransfer` | `/api/web/transfers`, `/api/web/transfers/{id}/receive` |
