# Contratos del Web BFF

> **Estado:** las verticales clínicas 1 (pacientes, atenciones, admisión, triage y signos vitales) y 2 (notas clínicas,
> diagnósticos y cierre de la atención) están implementadas; los endpoints marcados «Previsto» aún no.
>
> Los endpoints previstos describen la intención acordada para empezar a trabajar en paralelo (frontend y backend). Pueden evolucionar mediante **revisión arquitectónica**; cualquier cambio debe reflejarse en este documento dentro del mismo PR.

- **Cliente:** aplicación web React + TypeScript (áreas **Hospital** y **Administración**)
- **Proyecto:** `SCI-PH.Web.BFF`
- **Prefijo:** `/api/web`
- **Formato:** JSON. Los errores se devuelven como `application/problem+json` (ProblemDetails).
- **Autenticación:** JWT Bearer según [ADR-006](../adr/ADR-006-identity-security.md). El refresh token viaja solo en la cookie HttpOnly `__Host-sciph-refresh`; login, `mfa/verify`, refresh y logout exigen la cabecera `X-SCIPH-CSRF: 1`. Todos los endpoints requieren un usuario autenticado salvo los de sesión y recuperación de cuenta; los de `/api/web/admin` requieren además el permiso correspondiente. Endpoints en `/api/web/auth` ([detalle](../architecture/security.md#endpoints)).

## Web Hospital — verticales clínicas 1, 2 y 3 (implementado)

Reglas comunes:

- **Actor clínico:** `ICurrentUser → ClinicalStaff.IdentityUserId → ClinicStaffAffiliation activa → Clinic`. Las operaciones
  con contexto de clínica exigen un `ClinicalStaff` activo con afiliación activa (si no, 403). Con una afiliación la clínica
  se resuelve sola; con varias, `clinicId` es obligatorio (400 si falta) y siempre se valida contra las afiliaciones
  (403 si es ajena). **Administrator no tiene bypass clínico.** Pacientes no pertenecen a una clínica: registro y
  búsqueda solo requieren el permiso.
- **Enums como texto** (`"Emergency"`, `"InProgress"`, `"Immediate"`, `"HeartRate"`) solo en estos contratos; se rechazan
  números y miembros desconocidos (p. ej. `unit`). Auth/Admin no cambian.
- **Errores (ProblemDetails):** 400 validación / regla del Domain (fechas futuras o anteriores al inicio, signo vital
  fuera de rango, NN no permitido, Prehospital desde la web); 403 permiso o acceso clínico; 404 paciente/atención/
  medicamento/prescripción inexistente; 409 concurrencia, identificación repetida, unicidad, atención cerrada o en
  estado incompatible, medicamento inactivo, prescripción no emitida. Las respuestas 400 de reglas incluyen `field`
  cuando aplica (`details[i].dose` en las líneas de una prescripción).
- El primer registro clínico (triage, signo vital, nota, diagnóstico, prescripción o administración) mueve la atención
  `Created → InProgress` (transición del Domain), en la misma transacción.
- **Concurrencia:** todo registro clínico vuelve a comprobar los concurrency tokens de la atención (`Status`, `PatientId`)
  dentro de su misma transacción. Si otra transacción cerró o cambió la atención, el registro no se guarda → 409.

| Método | Ruta | Permiso | Request | Response |
|---|---|---|---|---|
| `GET` | `/api/web/clinical/context` | autenticado + ClinicalStaff | — | `{ staffId, clinics: [{ id, name }] }` |
| `POST` | `/api/web/patients` | `Patients.Create` | `{ givenNames, familyNames, dateOfBirth?, sex, identifier?: { type, value, issuer? } }` | 201 `PatientSummary` + `Location` · 409 identificador duplicado |
| `GET` | `/api/web/patients/{id}` | `Patients.Read` | — | `PatientSummary` (también lo usa el detalle del paciente) |
| `POST` | `/api/web/patients/search` | `Patients.Read` | `{ identifierType?, value }` (identificador: match por `NormalizedValue`; sin tipo: nombre, mín. 3) | `PatientSummary[]` (identificador enmascarado `***1234`, salvo `InternalCode`) |
| `GET` | `/api/web/encounters?clinicId=` | `Encounters.Read` | — | `EncounterSummary[]` abiertas (sin Closed/Cancelled/Transferred) |
| `GET` | `/api/web/encounters/{id}` | `Encounters.Read` | — | `EncounterSummary` |
| `POST` | `/api/web/encounters` | `Encounters.Create` | `{ type, patientId?, reason?, clinicId? }` (NN solo `Emergency`; `Prehospital` → 400) | 201 `EncounterSummary` |
| `POST` | `/api/web/encounters/{id}/identify` | `Patients.Identify` | `{ patientId }` | 204 · 409 ya identificada/concurrencia. Conserva `temporaryPatientCode`; `ClinicalAuditEntry` Identify |
| `GET` | `/api/web/admissions/encounters?clinicId=&unidentified=` | `Patients.Identify` | — | `AdmissionEncounterSummary[]` |
| `GET` | `/api/web/admissions/encounters/{id}` | `Patients.Identify` | — | `AdmissionEncounterSummary` |
| `GET` | `/api/web/encounters/{id}/triages` | `Encounters.Read` | — | `TriageSummary[]` (más reciente primero) |
| `POST` | `/api/web/encounters/{id}/triages` | `Triage.Write` (+ `VitalSigns.Write` si hay `measurements`) | `{ priority, reason?, observations?, performedAtUtc?, measurements?: [{ type, value, measuredAtUtc }] }` | 201 `TriageSummary` |
| `GET` | `/api/web/encounters/{id}/vital-signs` | `Encounters.Read` | — | `VitalSignSummary[]` (con `unit` del Domain) |
| `POST` | `/api/web/encounters/{id}/vital-signs` | `VitalSigns.Write` | `{ type, value, measuredAtUtc }` (sin `unit`) | 201 `VitalSignSummary` |
| `GET` | `/api/web/encounters/{id}/notes` | `Encounters.Read` | — | `ClinicalNoteSummary[]` (más reciente primero, con autor) |
| `POST` | `/api/web/encounters/{id}/notes` | `ClinicalNotes.Write` | `{ type, content, occurredAtUtc }` (`type`: `Assessment`, `Evolution`, `Other`; sin autor) | 201 `ClinicalNoteSummary` |
| `GET` | `/api/web/encounters/{id}/diagnoses` | `Encounters.Read` | — | `DiagnosisSummary[]` (principal primero) |
| `POST` | `/api/web/encounters/{id}/diagnoses` | `Diagnoses.Write` | `{ description, type, isPrimary?, codeSystem?, code? }` (un código exige su sistema) | 201 `DiagnosisSummary` |
| `POST` | `/api/web/encounters/{id}/close` | `Encounters.Close` | `{ dischargeNote? }` | 204 · 409 si el estado no permite cerrar (solo desde `InProgress`, `Received`, `InEvolution`) |
| `GET` | `/api/web/medications` | `Medications.Read` (+ ClinicalStaff activo) | — | `MedicationSummary[]` (catálogo global, activos e inactivos, por nombre) |
| `GET` | `/api/web/encounters/{id}/prescriptions` | `Encounters.Read` | — | `PrescriptionSummary[]` (más reciente primero, con líneas) |
| `POST` | `/api/web/encounters/{id}/prescriptions` | `Prescriptions.Write` | `{ notes?, details: [{ medicationId, dose, doseUnit, route, frequency, durationDays?, instructions? }] }` (1–20 líneas; sin prescriptor) | 201 `PrescriptionSummary` emitida · 404 medicamento · 409 medicamento inactivo |
| `GET` | `/api/web/encounters/{id}/medication-administrations` | `Encounters.Read` | — | `MedicationAdministrationSummary[]` (más reciente primero) |
| `POST` | `/api/web/encounters/{id}/medication-administrations` | `MedicationAdministrations.Write` | `{ medicationId, dose, doseUnit, route, administeredAtUtc, prescriptionId?, prescriptionDetailId?, notes? }` (sin responsable) | 201 `MedicationAdministrationSummary` · 404 prescripción/línea de otra atención · 409 prescripción no emitida o vencida |

- `EncounterSummary`: `{ id, patientId, patient: PatientSummary?, temporaryPatientCode, clinicId, type, status, reason, startedAtUtc, endedAtUtc, identifiedAtUtc, latestTriagePriority }`.
- `ClinicalNoteSummary`: `{ id, encounterId, type, content, occurredAtUtc, createdAtUtc, authorStaffId, authorDisplayName }`.
  Las notas son inmutables. La nota `Discharge` solo la escribe `/close`; `Prehospital` y `TransferSummary` pertenecen
  a los flujos paramédico y de traslado.
- `DiagnosisSummary`: `{ id, encounterId, description, codeSystem, code, type, isPrimary, recordedAtUtc, confirmedAtUtc, recordedByStaffId }`.
- Cerrar (`ClinicalEncounter.ChangeStatus(Closed)`) fija `endedAtUtc`, escribe la nota de egreso opcional y la auditoría
  en la misma transacción. Una atención cerrada no admite triage, signos, notas, diagnósticos, prescripciones ni
  administraciones (409); su historial sigue legible.
- `MedicationSummary`: `{ id, code, name, presentation, concentration, defaultRoute, status }`.
- `PrescriptionSummary`: `{ id, encounterId, status, notes, createdAtUtc, issuedAtUtc, validUntilUtc, cancelledAtUtc, prescribedByStaffId, details: [{ id, medicationId, medicationName, dose, doseUnit, route, frequency, durationDays, instructions }] }`.
  El `POST` ejecuta `Prescription.Draft`, `AddDetail` por línea e `Issue` en una transacción: no se guardan prescripciones
  vacías ni borradores.
- `MedicationAdministrationSummary`: `{ id, encounterId, medicationId, medicationName, prescriptionId, prescriptionDetailId, dose, doseUnit, route, administeredAtUtc, administeredByStaffId, status, notes }`.
  Una administración vinculada exige una prescripción emitida y vigente de la misma atención, y el medicamento de la línea.
- **Kardex:** no hay recurso propio; es la proyección de las prescripciones (con sus líneas) y las administraciones.
- `AdmissionEncounterSummary` (Recepción, **sin contenido clínico**): `{ encounterId, patientId, patientDisplayName, temporaryPatientCode, type, status, startedAtUtc, clinicId }`.
  Recepción no recibe `Encounters.Read`: crea con `POST /encounters` (que devuelve el resumen) y localiza/identifica por esta proyección.

## Web Hospital — vertical clínica 4: traslados (implementado)

`ClinicalTransfer`: `Requested → InTransit → Received` (o `Cancelled` antes de recibir). El acto clínico siempre lo
firma el `ClinicalStaff` de la sesión; nunca se aceptan `sentByStaffId`, `receivedByStaffId`, origen ni destino de
recepción desde el cliente.

| Método | Ruta | Permiso | Request | Response |
|---|---|---|---|---|
| `GET` | `/api/web/transfers/incoming?clinicId=` | `Transfers.Read` | — | `ClinicalTransferSummary[]` en `Requested`/`InTransit` con destino a las clínicas del usuario (o a `clinicId`, validada) |
| `GET` | `/api/web/transfers/{id}` | `Transfers.Read` | — | `ClinicalTransferSummary` (solo clínica origen o destino; si no, 403) |
| `POST` | `/api/web/encounters/{id}/transfers` | `Transfers.Manage` | `{ destinationClinicId, reason?, notes? }` (el origen es la clínica de la atención) | 201 `ClinicalTransferSummary` · 404 atención/clínica · 409 clínica inactiva, atención no trasladable o traslado activo · 400 mismo origen y destino |
| `POST` | `/api/web/transfers/{id}/dispatch` | `Transfers.Manage` (clínica origen) | — | 200 `ClinicalTransferSummary` · 409 si no está `Requested` o la atención cambió |
| `POST` | `/api/web/transfers/{id}/cancel` | `Transfers.Manage` (clínica origen) | — | 200 `ClinicalTransferSummary` · 409 si ya no está `Requested`/`InTransit` |
| `POST` | `/api/web/transfers/{id}/receive` | `Transfers.Manage` (clínica destino) | — | 200 `ClinicalTransferSummary` · 409 si no está `InTransit` (p. ej. ya recibido) |

- **Solicitar** no cambia la atención (el paciente aún no salió). Una sola transferencia activa (`Requested`/`InTransit`,
  `ClinicalTransfer.ActiveStatuses`) por atención: Application lo rechaza con 409 y PostgreSQL lo garantiza ante
  solicitudes simultáneas con el índice único filtrado `UX_ClinicalTransfers_ActivePerEncounter` (la violación se
  traduce a 409). `Received`, `Completed` y `Cancelled` no bloquean un traslado posterior.
- **Cancelar** (clínica origen; el Domain no registra motivo): un traslado `Requested` solo pasa a `Cancelled` (la
  atención no había cambiado); uno `InTransit` pasa a `Cancelled` y la atención vuelve `Transferred → InProgress` en la
  clínica de origen. Recibidos, completados o ya cancelados → 409. Queda auditado como `Transfer`.
- **Despachar**: traslado `InTransit` y atención `Transferred` en una transacción. Mientras va en camino la atención sigue
  en la clínica de origen (y sale de su lista de abiertas).
- **Recibir**: traslado `Received` y `ClinicalEncounter.ReceiveAtClinic` en una transacción: la **misma** atención pasa a
  `Received` y a la clínica destino (también una prehospitalaria sin clínica). Conserva paciente, código NN e historial.
  La clínica destino la continúa con las reglas clínicas normales (`Received → InProgress/InEvolution/Closed`); la de
  origen conserva la traza (traslado y auditoría `Transfer`) pero pierde el acceso clínico.
- `Complete` no se expone: el Domain no define cuándo se completa un traslado recibido.
- **Concurrencia:** el `Status` del traslado y el de la atención son concurrency tokens. Dos recepciones, dos despachos,
  dos cancelaciones, cancelar contra recibir o despachar, o un cierre simultáneo nunca se mezclan: se aplica una
  operación y la otra recibe 409 (o se aplica después, en orden, si sigue siendo válida; p. ej. cancelar un traslado
  que se acaba de despachar). Cancelar un traslado `Requested` no toca la atención y no compite con cerrarla.
- `ClinicalTransferSummary`: `{ id, encounterId, status, originClinic: { id, name }?, destinationClinic: { id, name }, ambulance: { id, code, callSign }?, requestedAtUtc, sentAtUtc, sentByStaffId, receivedAtUtc, receivedByStaffId, cancelledAtUtc, patient: PatientSummary?, temporaryPatientCode, reason, notes, latestTriagePriority, encounterStatus }`
  (identificador del paciente enmascarado).

---

## Web Hospital — huella digital (implementado)

Registro (enrolamiento) de huellas de un paciente en un lector ([ADR-010](../adr/ADR-010-fingerprint-enrollment.md)) y su
revocación ([ADR-011](../adr/ADR-011-fingerprint-revocation.md)).
Permiso `Biometrics.Manage` **y** personal clínico activo que pueda ver al paciente (`Patients.Read`); sin atajo por
rol. El lector guarda la plantilla en su sensor; SCI-PH solo guarda a qué paciente pertenece. Nunca se devuelven el slot,
la referencia interna (`SubjectReference`), plantillas, claves ni scopes.

| Método | Ruta | Request | Response |
|---|---|---|---|
| `GET` | `/api/web/fingerprint-enrollers` | — | `[{ id, code, name, lastSeenAtUtc }]`: lectores **activos** con `Device.Fingerprint.Enroll` **y** `Device.Fingerprint.Identify` |
| `GET` | `/api/web/patients/{patientId}/fingerprints` | — | `[PatientFingerprint]`, más recientes primero |
| `POST` | `/api/web/patients/{patientId}/fingerprints/enrollments` | `{ clientRequestId, deviceId }` | 201 `FingerprintEnrollment` · 200 reintento · 400 · 403 · 404 paciente o lector no elegible · 409 |
| `GET` | `/api/web/patients/{patientId}/fingerprints/enrollments/{enrollmentId}` | — | `FingerprintEnrollment` (404 si es de otro paciente) |
| `POST` | `/api/web/patients/{patientId}/fingerprints/enrollments/{enrollmentId}/cancel` | — | 200 `FingerprintEnrollment` · 409 si ya está `Enrolled` |
| `POST` | `/api/web/patients/{patientId}/fingerprints/{referenceId}/revoke` | `{ reason? }` (opcional; sin cuerpo también) | 200 `PatientFingerprint` (también si ya estaba revocada) · 400 · 403 · 404 |

- `PatientFingerprint`: `{ id, status: Active|Revoked, reader: { code, name, available }?, registeredAtUtc, revokedAtUtc,
  cleanup: null|Pending|Completed|AttentionRequired|NotRequired }`. `cleanup` es `null` mientras está activa y, revocada,
  el estado (derivado) del borrado en su lector: `AttentionRequired` si el lector está revocado, no existe, no puede
  borrar o lleva 5 intentos fallidos, o si la referencia no permite saber lector y slot; `NotRequired` si se revocó antes
  de existir el borrado automático.

- `FingerprintEnrollment`: `{ id, status: Pending|Enrolled|Failed|Expired|Cancelled, readerId, expiresAtUtc, failure:
  Failed|LowQuality|SensorError (solo Failed), biometricReferenceId (solo Enrolled) }`.
- **Abrir:** una sesión `Pending` de 120 s (duración del prototipo), su auditoría y el recibo en una transacción. No se
  aceptan `patientId`, `staffId`, `slot`, `subjectReference`, `provider`, `modality`, `status` ni fechas en el cuerpo
  (400). 409 si el paciente ya tiene una huella activa en ese lector, si ya hay un registro pendiente del paciente o del
  lector, o si la clave se usó con otro paciente o lector. Idempotencia: `fingerprint-enrollment.open.v1` (hash del
  paciente y el lector; el recibo apunta a la sesión).
- **Resultado** (lo informa el lector, ver el Device API): `Enrolled` crea la referencia (`RegisteredByStaffId` = quien
  abrió el registro) en la misma transacción; `Failed`/`LowQuality`/`SensorError` terminan en `Failed`. Otro intento es
  otro registro.
- **Cancelar:** `Pending → Cancelled` (auditada una sola vez); ya `Cancelled`, `Expired` o `Failed`: 200 sin cambios;
  `Enrolled`: 409 (la referencia no se toca; la revocación es otra operación).
- **Revocar:** `Active → Revoked` con su auditoría y **un** trabajo de borrado para el lector, en una transacción; desde
  esa respuesta la huella no identifica a nadie. `reason`: `PatientRequest`, `EnrollmentError`, `ReaderReplaced` u
  `Other`, solo en la auditoría (sin texto libre). Otro campo (`slot`, `subjectReference`, `deviceId`, `status`,
  `revokedAt`, `staffId`, `patientId`) o un motivo desconocido: 400. Ya revocada: 200 con su estado y nada escrito (nunca
  un trabajo de reparación). Referencia de otro paciente, no de huella o inexistente: 404. Sin reintento manual de la
  limpieza: el lector reintenta solo.
- **Vencimiento:** un registro `Pending` vencido se cierra como `Expired` al consultarlo o cancelarlo.
- La UI (detalle del paciente, `/hospital/pacientes/{id}`) pide una confirmación operacional antes de abrir, consulta
  cada segundo mientras está `Pending` y deja de hacerlo al terminar o al salir. No es un registro de consentimiento.
  "Revocar huella" pide confirmación (con motivo opcional), muestra la huella `Revocada` al instante y la limpieza del
  lector aparte; nunca la presenta como válida mientras se limpia.

---

## Web Hospital — rostro (implementado)

Verificación facial **1:1 asistida** de un paciente conocido ([ADR-012](../adr/ADR-012-face-verification.md)): solo
contra la referencia facial activa de **ese** paciente, nunca una búsqueda entre pacientes. **Sin prueba de vida:** ayuda
al personal presente; no es una autenticación ni una prueba legal de identidad. No identifica atenciones ni cambia nada
clínico. Personal clínico activo que pueda ver al paciente (`Patients.Read`); sin atajo por rol. Nunca se devuelven la
referencia del proveedor (`SubjectReference`), la plantilla, el puntaje ni el umbral.

| Método | Ruta | Permiso | Request | Response |
|---|---|---|---|---|
| `GET` | `/api/web/patients/{patientId}/faces` | `Biometrics.Manage` o `Patients.Identify` | — | `[PatientFace]`, más recientes primero |
| `POST` | `/api/web/patients/{patientId}/faces/enroll` | `Biometrics.Manage` | multipart: `capture`, `clientRequestId` | 201 `{ outcome: Enrolled, face }` · 200 reintento o `{ outcome: NoFace\|MultipleFaces\|LowQuality, face: null }` · 400 · 403 · 404 · 409 · 503 |
| `POST` | `/api/web/patients/{patientId}/faces/verify` | `Patients.Identify` | multipart: `capture`, `clientRequestId` | 200 `{ verificationId, outcome: Match\|NoMatch\|NoFace\|MultipleFaces\|LowQuality }` · 400 · 403 · 404 · 409 sin rostro activo · 503 (también rostro de otro proveedor) |
| `POST` | `/api/web/patients/{patientId}/faces/{referenceId}/revoke` | `Biometrics.Manage` | `{ reason? }` (opcional; sin cuerpo también) | 200 `PatientFace` (también si ya estaba revocado) · 400 · 403 · 404 |

- `PatientFace`: `{ id, status: Active|Revoked, provider, registeredAtUtc, revokedAtUtc }`. `provider` es el nombre
  estable del proveedor (la UI muestra una etiqueta).
- **Captura:** `multipart/form-data` con exactamente `capture` (la imagen) y `clientRequestId`; cualquier otro campo
  (`patientId`, `provider`, `threshold`, `referenceId`, otro archivo) es 400; JSON es 415. Máximo 2 MB; JPEG o PNG por
  sus bytes y decodificación real; lado menor ≥ 480 px, ningún lado > 4096 px. El servidor aplica la orientación, quita
  los metadatos y re-codifica; la imagen nunca se guarda.
- **Registro:** una referencia facial activa por paciente (409 si ya existe, también con registros simultáneos). El mismo
  `clientRequestId` con la misma imagen devuelve la misma referencia sin volver a llamar al proveedor; con otra imagen, 409.
- **Verificación:** todos los resultados biométricos son 200. El mismo `clientRequestId` con la misma imagen devuelve el
  resultado registrado sin llamar al proveedor; con otra imagen, 409. Se registra en la auditoría (`FaceVerification`).
- **Revocación:** `reason`: `PatientRequest`, `EnrollmentError`, `ReferenceOutdated` u `Other` (solo en la auditoría).
  Desde esa respuesta el rostro no se usa para verificar; la plantilla del proveedor se borra después con un job durable
  que se reintenta hasta completarse (no cambia la respuesta).
- **503:** el proveedor facial no está disponible, su plantilla falta o no se puede leer, o el rostro activo pertenece a
  otro proveedor o versión de algoritmo (hay que registrarlo de nuevo); mensaje genérico, sin detalles internos y nunca
  un `NoMatch`.
- La UI usa la cámara del equipo (`getUserMedia`, sin selector de archivos), la apaga en cuanto toma la foto y no guarda
  la captura en el navegador. Muestra solo "Coincide", "No coincide" o la calidad; nunca un porcentaje.

---

## Web Hospital — resumen asistido por IA (implementado)

Resumen **asistido** de una atención con puntos de documentación a revisar ([ADR-013](../adr/ADR-013-clinical-ai-assistant.md)).
Generado por IA, **requiere revisión profesional**, de solo lectura y **efímero**: no se guarda, no crea notas,
diagnósticos ni órdenes y no cambia la atención. Mismo acceso que leer la atención; sin permiso propio.

| Método | Ruta | Permiso | Request | Response |
|---|---|---|---|---|
| `POST` | `/api/web/encounters/{encounterId}/ai-summary` | `Encounters.Read` + acceso a la atención | sin cuerpo o `{}` | 200 `EncounterAiSummary` · 400 · 401 · 403 · 404 · 429 · 503 |

- `EncounterAiSummary`: `{ aiGenerated: true, requiresClinicianReview: true, summary, sections: { course, vitalSigns,
  medications, procedures, notes, transfer }, reviewPoints: [{ kind, text, sources }], notDocumented: [string],
  generatedAtUtc, provider, model, promptVersion, sourceFingerprint, truncated }`. Cada sección es `{ text, sources }` o
  `null`; `kind`: `MissingDocumentation`, `Inconsistency` o `PendingItem`; `sources`: `[{ type, id }]` con `type` entre
  `Triage`, `VitalSign`, `ClinicalNote`, `Diagnosis`, `Prescription`, `MedicationAdministration`, `ClinicalProcedure` y
  `ClinicalTransfer`. Sin diagnóstico sugerido, recomendación, tratamiento, gravedad ni confianza.
- **Registros relacionados:** existen en esta atención (el servidor lo comprueba), pero no demuestran que la frase esté
  respaldada por ellos; sirven para navegar.
- **Instantánea** de los registros en `generatedAtUtc` (`sourceFingerprint` identifica esa entrada); no se actualiza sola.
  `truncated`: la atención tenía más registros de los que admite el resumen y solo se incluyeron los más recientes.
- **Cuerpo sin opciones:** cualquier miembro (`model`, `prompt`, `temperature`, `provider`, `maxTokens`, `systemPrompt`,
  `patientId`, datos clínicos) es 400. Proveedor, modelo, prompt y límites son del servidor.
- **429:** 10 generaciones por minuto y una a la vez por usuario, con `Retry-After` (también cuentan las peticiones
  rechazadas).
- **503:** proveedor deshabilitado o no disponible, tiempo agotado, filtro de contenido, negativa, cuota o límite del
  proveedor, o respuesta del proveedor inválida; mensaje genérico, sin detalles internos. Nunca se propaga el estado HTTP
  del proveedor (un 429 del proveedor también es 503; solo nuestro limitador responde 429).
- `provider`/`model`: el proveedor configurado en el servidor (`SCIPH-AiFake`/`fake-summary-v1`, o `AzureOpenAI` y la
  versión del modelo desplegado, [ADR-014](../adr/ADR-014-clinical-ai-real-provider.md)); nunca el nombre del despliegue.
  Con un proveedor externo, repetir la misma petición dentro de 10 minutos, con la misma entrada, devuelve el mismo resumen
  (mismo `generatedAtUtc`) sin generarlo de nuevo.
- Cada intento queda en la auditoría (`ClinicalAiEncounterSummary`: `Read`, o `Export` cuando la entrada se envió a un
  proveedor externo; éxito o fallo) solo con metadatos: nunca el contenido generado ni datos clínicos.
- La UI muestra todo como texto plano, con el aviso fijo "Generado por IA · Requiere revisión profesional" y la hora de
  generación.

---

## Web Administration — contratos iniciales previstos

> Los endpoints marcados **Implementado** existen; el resto son contratos previstos que pueden evolucionar
> mediante revisión arquitectónica. Detalle de seguridad en [security.md](../architecture/security.md#endpoints).

- **Prefijo:** `/api/web/admin`
- **Autorización:** permiso específico por endpoint (`Users.Manage`, `Accounts.InvitePatient`, `Devices.Manage`, `Audit.Read`,
  `Clinics.Manage`, `ClinicalStaff.Manage`).
- **Paginación (propuesta):** los listados aceptan `page` y `pageSize` y devuelven `{ "items": [...], "page": 1, "pageSize": 20, "total": 0 }`.

| Método | Ruta | Descripción | Estado |
|---|---|---|---|
| `GET` | `/api/web/admin/users` | Lista cuentas de usuario | Implementado |
| `GET` | `/api/web/admin/users/{id}` | Detalle de una cuenta | Implementado |
| `POST` | `/api/web/admin/users/{id}/activate` · `/deactivate` · `/revoke-sessions` | Estado y sesiones de una cuenta | Implementado |
| `PUT` | `/api/web/admin/users/{id}/roles` | Reemplaza los roles de una cuenta | Implementado |
| `POST` | `/api/web/admin/invitations/patients` · `/staff` | Invita a un paciente o a personal existente | Implementado |
| `POST` | `/api/web/admin/invitations/{id}/reissue` · `/revoke` | Reemite o revoca una invitación | Implementado |
| `GET` | `/api/web/admin/roles` | Lista los roles y sus permisos | Implementado |
| `GET` | `/api/web/admin/audit` | Consulta el registro de auditoría | Previsto |
| `GET` · `POST` | `/api/web/admin/devices` | Lista o registra dispositivos ESP32 | Implementado |
| `POST` | `/api/web/admin/devices/{id}/rotate-secret` · `/revoke` | Rota la clave o revoca un dispositivo | Implementado |

No existe `POST /api/web/admin/users`: las cuentas se crean por **invitación** a un `Patient` o `ClinicalStaff`
existente (ver [ADR-006](../adr/ADR-006-identity-security.md), decisión 3).

Estos endpoints gestionan **cuentas** (`ApplicationUser`, schema `identity`), no pacientes.

### Administración clínica: clínicas, personal y afiliaciones (implementado)

Datos clínico-organizativos (schema `clinical`), separados de Identity. Solo lo que admite el Domain: **no hay edición
(PUT/PATCH) ni borrado**; el historial se conserva. Enums como texto; miembros desconocidos → 400.

| Método | Ruta | Permiso | Request | Response |
|---|---|---|---|---|
| `GET` | `/api/web/admin/clinics?search=&status=` | `Clinics.Manage` | — | `ClinicAdminSummary[]` por nombre |
| `GET` | `/api/web/admin/clinics/{id}` | `Clinics.Manage` | — | `ClinicAdminSummary` · 404 |
| `POST` | `/api/web/admin/clinics` | `Clinics.Manage` | `{ name, type, province, municipality, facilityCode?, address?, phoneNumber? }` (`Clinic.Register`) | 201 · 409 código de establecimiento duplicado |
| `POST` | `/api/web/admin/clinics/{id}/activate` · `/deactivate` | `Clinics.Manage` | — | 204 (idempotente) · 404 |
| `GET` | `/api/web/admin/staff?search=&status=` | `ClinicalStaff.Manage` | — | `ClinicalStaffAdminSummary[]` |
| `GET` | `/api/web/admin/staff/{id}` | `ClinicalStaff.Manage` | — | `ClinicalStaffAdminSummary` · 404 |
| `POST` | `/api/web/admin/staff` | `ClinicalStaff.Manage` | `{ givenNames, familyNames, type, professionalLicenseNumber?, specialty?, phoneNumber? }` (`ClinicalStaff.Register`; sin cuenta y sin email) | 201 · 409 licencia duplicada |
| `POST` | `/api/web/admin/staff/{id}/activate` · `/deactivate` | `ClinicalStaff.Manage` | — | 204 (idempotente; la cuenta no se toca) · 404 |
| `GET` | `/api/web/admin/staff/{id}/affiliations` | `ClinicalStaff.Manage` | — | `ClinicStaffAffiliationSummary[]` (abiertas primero) · 404 |
| `POST` | `/api/web/admin/staff/{id}/affiliations` | `ClinicalStaff.Manage` | `{ clinicId }` (empieza ahora) | 201 · 404 personal/clínica · 409 inactivos o afiliación abierta ya existente |
| `POST` | `/api/web/admin/staff/{id}/affiliations/{affiliationId}/end` | `ClinicalStaff.Manage` | — | 200 (termina ahora; queda como historial) · 404 · 409 ya finalizada |

- `ClinicAdminSummary`: `{ id, name, type, facilityCode, province, municipality, address, phoneNumber, status, activeStaffCount }`.
- `ClinicalStaffAdminSummary`: `{ id, givenNames, familyNames, type, status, professionalLicenseNumber, specialty, phoneNumber, accountLinked, identityUserId, activeClinics: [{ id, name }] }`.
  Solo indica si hay cuenta vinculada; los datos de la cuenta siguen en `/api/web/admin/users` (`Users.Manage`).
- `ClinicStaffAffiliationSummary`: `{ id, clinicId, clinicName, clinicStatus, startedAtUtc, endedAtUtc, isActive }`.
- **Cuenta del personal:** se crea con `POST /api/web/admin/invitations/staff` (`Users.Manage`, roles de
  `SecurityRoles.InvitableStaff`): solo personal activo y sin cuenta (si ya está vinculado → 409). Al activar la invitación
  se fija `ClinicalStaff.IdentityUserId`; una cuenta no puede vincularse a dos miembros del personal (índice único → 409).
- **Actor clínico:** el personal resuelve contexto clínico solo si está activo y tiene afiliaciones abiertas a clínicas
  activas. Desactivar una clínica o un miembro del personal **no** termina sus afiliaciones. Con varias clínicas activas
  las operaciones con contexto exigen `clinicId`.
- **Duplicados bajo concurrencia:** los índices únicos existentes (código de establecimiento, licencia, cuenta vinculada,
  afiliación abierta `UX_ClinicStaffAffiliations_Active`) se traducen a 409; ninguna otra excepción de base de datos.

---

### `GET /api/web/admin/users`

**Query:** `search` (correo o usuario), `page` (desde 1), `pageSize` (1–100, 20 por defecto).

**Respuesta `200 OK`:**

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000000",
      "userName": "…",
      "email": "…",
      "emailConfirmed": true,
      "isActive": true,
      "twoFactorEnabled": false,
      "lockedOutUntilUtc": null,
      "roles": ["…"],
      "createdAtUtc": "2026-01-01T00:00:00Z",
      "lastLoginAtUtc": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 0
}
```

**Errores:** `401`, `403`.

---

### `GET /api/web/admin/users/{id}`

**Respuesta `200 OK`:** un elemento del listado. **Errores:** `401`, `403`, `404`.

---

### `PUT /api/web/admin/users/{id}/roles`

Reemplaza el conjunto completo de roles de la cuenta.

**Body (forma propuesta):**

```json
{
  "roles": ["…"]
}
```

**Respuestas:** `204 No Content`, `400` (rol inexistente o `Patient` combinado con otro rol), `401`, `403`, `404`,
`409` (quitarse el propio rol de administrador o dejar el sistema sin administradores activos).

Cada cambio de roles revoca las sesiones de la cuenta y queda en la auditoría (`RoleChanged`, con el actor).

---

### `GET /api/web/admin/roles`

Requiere el permiso `Users.Manage`.

**Respuesta `200 OK`:**

```json
[
  { "name": "Doctor", "permissions": ["Patients.Read", "…"] }
]
```

**Errores:** `401` (sin autenticar), `403` (sin permiso).

---

### `GET /api/web/admin/audit`

**Query (propuesta):** `page`, `pageSize`, `from`, `to`, `userId`, `action`.

**Respuesta `200 OK` (forma propuesta):**

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000000",
      "occurredAt": "2026-01-01T00:00:00Z",
      "userId": "00000000-0000-0000-0000-000000000000",
      "action": "…",
      "target": "…",
      "result": "…"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 0
}
```

El registro de auditoría es de solo lectura. Su modelo lo define el Rol 3.

**Errores previstos:** `401`, `403`.

---

### `GET /api/web/admin/devices`

Lista los dispositivos registrados (permiso `Devices.Manage`). Nunca incluye claves.

**Respuesta `200 OK`:**

```json
[
  {
    "id": "…",
    "deviceCode": "ESP32-001",
    "name": "…",
    "scopes": ["Device.Heartbeat"],
    "status": 1,
    "firmwareVersion": "0.0.0",
    "createdAtUtc": "2026-01-01T00:00:00Z",
    "lastSeenAtUtc": "2026-01-01T00:00:00Z",
    "rotatedAtUtc": null,
    "revokedAtUtc": null
  }
]
```

`lastSeenAtUtc` y `firmwareVersion` provienen de `POST /api/device/heartbeat`. `POST /api/web/admin/devices`
(`deviceCode`, `name`, `scopes`) y `POST /{id}/rotate-secret` devuelven `{ id, deviceCode, secret }`: la clave
se muestra **una sola vez**. Ver [ADR-007](../adr/ADR-007-device-authentication.md).

**Errores:** `400` (scopes), `401`, `403`, `404`, `409` (código duplicado).
