# Web Hospital (Rol 4) — contratos que usa el frontend

Estado: **todo el módulo hospitalario integrado con el Web BFF real** (verticales clínicas 1 a 4: pacientes, atenciones, admisión/NN, triage,
signos vitales, notas clínicas, diagnósticos, cierre de la atención, catálogo de medicamentos, prescripciones, Kardex y
traslados entrantes). El adaptador MOCK solo se usa con `VITE_CLINICAL_API_MODE=mock`; la UI ya no marca nada como
«Simulación» fuera de ese modo.

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
| Recepción | `Encounters.Create`, `Patients.Read` | Buscar: `Patients.Read` · registrar: `Patients.Create` · abrir atención: `Encounters.Create` · admisión e identificar NN: `Patients.Identify` · traslados entrantes: `Transfers.Read` · registrar llegada: `Transfers.Manage` |
| Pacientes | `Patients.Read` | Registrar: `Patients.Create` |
| Atenciones / Expediente | `Encounters.Read` | Diagnóstico: `Diagnoses.Write` · nota: `ClinicalNotes.Write` · resumen asistido por IA: `Encounters.Read` (sin permiso propio) |
| Triage | `Triage.Write` | Signos vitales en el triage o sueltos: `VitalSigns.Write` |
| Medicación y egreso | `Medications.Read`, `Prescriptions.Write`, `MedicationAdministrations.Write` | Emitir prescripción: `Prescriptions.Write` · registrar administración: `MedicationAdministrations.Write` · cerrar la atención: `Encounters.Close`, solo desde `InProgress`/`Received`/`InEvolution`. Ninguna escritura en una atención cerrada |
| Transferencias | `Transfers.Read` | Registrar llegada (solo traslados `InTransit`): `Transfers.Manage`. Abrir la atención recibida: `Encounters.Read` |

Recepción **no** tiene `Encounters.Read`: tras crear una atención se queda en Recepción con el resumen devuelto por el
`POST` y la ve en «Atenciones en admisión» (proyección sin datos clínicos).

## Endpoints reales (verticales clínicas 1 a 4)

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
| AI summary | POST (sin cuerpo) | `/api/web/encounters/{id}/ai-summary` | Expediente (resumen asistido, ADR-013) |
| Encounters | POST | `/api/web/encounters/{id}/close` | Medicación y egreso |
| Medications | GET | `/api/web/medications` | Medicación y egreso |
| Prescriptions | GET · POST | `/api/web/encounters/{id}/prescriptions` | Medicación y egreso |
| MedicationAdministrations | GET · POST | `/api/web/encounters/{id}/medication-administrations` | Medicación y egreso (Kardex) |
| Transfers | GET | `/api/web/transfers/incoming?clinicId=` | Recepción, Transferencias |
| Transfers | POST | `/api/web/transfers/{id}/receive` | Recepción, Transferencias |
| Documents | GET | `/api/web/encounters/{id}/documents` | Expediente (Documents.Read) |
| Documents | POST (multipart: `file`, `type`, `clientRequestId`) | `/api/web/encounters/{id}/documents` | Expediente (Documents.Upload + lectura de la atención; PDF/PNG/JPEG por contenido real, 10 MB) |
| Documents | GET | `/api/web/encounters/{id}/documents/{documentId}/content` | Expediente: descarga como adjunto (`nosniff`) |

El **Kardex** no es un recurso propio: la pantalla lo compone con las prescripciones emitidas (con sus líneas) y las
administraciones registradas. Una administración puede vincularse a una línea prescrita (el medicamento debe ser el
prescrito) o registrarse sin prescripción. Prescriptor y responsable de la administración los fija el backend.

**Traslados.** La lista entrante muestra los traslados `Requested` e `InTransit` con destino a la clínica del usuario;
solo uno `InTransit` admite «Registrar llegada». Al recibirlo, la misma atención pasa a esta clínica (estado
`Received`) y la clínica de origen deja de tener acceso clínico. Quién recibe y el destino los fija el backend. La
solicitud, el despacho y la cancelación (`POST /api/web/encounters/{id}/transfers`,
`POST /api/web/transfers/{id}/dispatch`, `POST /api/web/transfers/{id}/cancel`) existen en el backend para la clínica de
origen y la app paramédica; el hospital todavía no tiene pantalla para ellos.

## Aún simulado (MOCK)

Nada en el módulo hospitalario. `mockClinicalApi.ts` se mantiene solo para `VITE_CLINICAL_API_MODE=mock`.
