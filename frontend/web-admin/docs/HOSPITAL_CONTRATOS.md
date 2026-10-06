# Web Hospital (Rol 4) — contratos que necesita el frontend

Estado: **propuesta del frontend, pendiente de validar por el Backend Lead.** Ninguno de estos endpoints existe todavía
salvo los de autenticación. Mientras tanto la UI usa el adaptador MOCK `src/features/clinical/api/mockClinicalApi.ts`
(`CLINICAL_API_IS_MOCK = true`): datos ficticios en memoria, la cabecera muestra «Simulación» y cada confirmación lo
indica. No se hace `fetch` a rutas inexistentes.

Los tipos de request/response están en `src/features/clinical/types/clinical.types.ts` y siguen el Domain
(`src/Core/SCI-PH.Domain`). El puerto que implementará el adaptador HTTP es `ClinicalApi` (`src/features/clinical/api/clinicalApi.ts`).

## Reglas comunes

- Prefijo `/api/web`, autenticación Bearer con el cliente compartido del Rol 5 (token en memoria, refresh por la cookie
  `__Host-sciph-refresh`, CSRF según ADR-006). El módulo clínico no maneja tokens.
- Errores como ProblemDetails. La UI distingue 401, 403, 404, 409 y 5xx; un error nunca se muestra como «sin datos».
- **Autoría desde la sesión:** el cliente no envía `author`, `performedBy`, `prescribedBy` ni `administeredBy`.
- **Enums:** la UI usa los nombres de los miembros C# (`"Immediate"`, `"InProgress"`…). Hoy el BFF no registra
  `JsonStringEnumConverter`; hay que decidir si se serializan como texto (propuesto) o como número.
- **Identificadores sensibles fuera de la URL:** la búsqueda de pacientes va por POST con cuerpo.

## Permisos por pantalla (permisos reales de Permissions.cs)

El módulo usa la autenticación compartida (`src/auth`): `/hospital/*` está detrás de `RequireAuth` y cada pantalla de
`RequirePermission`. Tras el login, `APP_MODULES` (`src/auth/modules.ts`) envía a `/hospital` a quien tenga permisos
clínicos y no de administración (p. ej. Receptionist, Doctor, Nurse, Paramedic); el Administrator aterriza en Admin y
puede cambiar de módulo desde el menú.

| Pantalla | Acceso (cualquiera) | Acciones dentro |
|---|---|---|
| Recepción | `Encounters.Create`, `Patients.Read` | Buscar: `Patients.Read` · abrir atención: `Encounters.Create` · traslados: `Transfers.Read` |
| Pacientes | `Patients.Read` | Pendiente de backend |
| Atenciones / Expediente | `Encounters.Read` | Diagnóstico: `Diagnoses.Write` · nota: `ClinicalNotes.Write` |
| Triage | `Triage.Write` | Signos vitales: `VitalSigns.Write` |
| Medicación y egreso | `Medications.Read`, `Prescriptions.Write`, `MedicationAdministrations.Write` | Prescribir: `Prescriptions.Write` · cerrar atención: `Encounters.Close` |
| Transferencias | `Transfers.Read` | Registrar llegada: `Transfers.Manage` |

Huecos de permisos (no se inventan; a decidir con el Backend Lead):

- No hay `Triage.Read`, `VitalSigns.Read`, `ClinicalNotes.Read`, `Diagnoses.Read` ni `MedicationAdministrations.Read`:
  la lectura del expediente se cubre con `Encounters.Read` y la del Kardex con `Medications.Read`.
- Receptionist tiene `Encounters.Create` pero no `Encounters.Read`: puede abrir una atención (también NN) pero no verla
  después. La UI se queda en Recepción en ese caso.
- No hay permiso para identificar a un paciente dentro de una atención NN distinto de `Patients.Identify`.

## Endpoints

| Área | Método | Ruta | Request | Response | Pantalla | Permiso |
|---|---|---|---|---|---|---|
| Patients | POST | `/api/web/patients/search` | `PatientSearchRequest` `{ identifierType?, value }` | `PatientSummary[]` | Recepción | Patients.Read |
| Encounters | GET | `/api/web/encounters?status=` | — | `ClinicalEncounterSummary[]` | Atenciones, Triage | Encounters.Read |
| Encounters | GET | `/api/web/encounters/{id}` | — | `ClinicalEncounterSummary` | Expediente | Encounters.Read |
| Encounters | POST | `/api/web/encounters` | `CreateEncounterRequest` `{ type, patientId \| null, reason }` | `ClinicalEncounterSummary` (con `temporaryPatientCode` si es NN) | Recepción | Encounters.Create |
| Encounters | POST | `/api/web/encounters/{id}/close` | `CloseEncounterRequest` `{ dischargeNote \| null }` | 204 · 409 si el estado no lo permite | Medicación y egreso | Encounters.Close |
| Triage | GET · POST | `/api/web/encounters/{id}/triages` | `CreateTriageRequest` `{ priority, reason, observations, vitalSigns[] }` | `Triage[]` · `Triage` | Triage | Encounters.Read · Triage.Write |
| VitalSigns | GET | `/api/web/encounters/{id}/vital-signs` | — | `VitalSign[]` `{ type, value, unit, measuredAtUtc, triageId }` | Triage | Encounters.Read |
| ClinicalNotes | GET · POST | `/api/web/encounters/{id}/notes` | `{ type, content, occurredAtUtc }` | `ClinicalNote[]` · `ClinicalNote` | Expediente | ClinicalNotes.Write |
| Diagnoses | GET · POST | `/api/web/encounters/{id}/diagnoses` | `{ code, codeSystem, description, type, isPrimary }` | `Diagnosis[]` · `Diagnosis` | Expediente | Diagnoses.Write |
| Medications | GET | `/api/web/medications` | — | `Medication[]` | Medicación | Medications.Read |
| Prescriptions | GET · POST | `/api/web/encounters/{id}/prescriptions` | `{ notes, details: [{ medicationId, dose, doseUnit, route, frequency, durationDays, instructions }] }` | `Prescription[]` · `Prescription` | Medicación | Prescriptions.Write |
| MedicationAdministrations | GET | `/api/web/encounters/{id}/medication-administrations` | — | `MedicationAdministration[]` | Medicación (Kardex) | Medications.Read |
| Transfers | GET | `/api/web/transfers?direction=incoming` | — | `ClinicalTransfer[]` | Recepción, Transferencias | Transfers.Read |
| Transfers | POST | `/api/web/transfers/{id}/receive` | — | 204 · 409 si no está `InTransit` | Recepción, Transferencias | Transfers.Manage |

`GET /api/web/transfers` y `POST /api/web/transfers/{id}/receive` ya figuran como «Previsto» en
`docs/api/web-bff-contracts.md`; la forma de `ClinicalTransfer` de este documento completa su respuesta.

## Pendiente de definir con el Backend Lead

- Contexto de clínica activa del usuario (qué hospital ve las atenciones y los traslados entrantes).
- Identificación posterior de un paciente NN (`ClinicalEncounter.IdentifyPatient`) y su pantalla.
- Registro de administración de medicamentos (`MedicationAdministrations.Write`): el Kardex hoy es de solo lectura.
- Pacientes (detalle, identificadores, historial), procedimientos, resultados y documentos: sin pantalla en esta iteración.
