# Contratos del Mobile BFF

> **Estado: contratos iniciales previstos, salvo los marcados como _Implementado_** (autenticación; apertura, lectura,
> identificación por documento y signos vitales de la emergencia prehospitalaria).
>
> Estos endpoints describen la intención acordada para empezar a trabajar en paralelo (app móvil y backend). Pueden evolucionar mediante **revisión arquitectónica**; cualquier cambio debe reflejarse en este documento dentro del mismo PR.

- **Cliente:** aplicación .NET MAUI (flujos **Patient** y **Paramedic**)
- **Proyecto:** `SCI-PH.Mobile.BFF`
- **Prefijo:** `/api/mobile`
- **Formato:** JSON. Los errores se devuelven como `application/problem+json` (ProblemDetails).
- **Autenticación:** JWT Bearer + refresh token según [ADR-006](../adr/ADR-006-identity-security.md). Login con usuario/contraseña y sesión en `/api/mobile/auth` ([detalle](../architecture/security.md#endpoints)); el acceso por QR y verificación facial sigue previsto y emitirá la misma sesión. La cuenta autenticada se consulta en `GET /api/mobile/auth/me`.

## Patient — contratos iniciales previstos

| Método | Ruta | Descripción | Autenticación | Estado |
|---|---|---|---|---|
| `POST` | `/api/mobile/auth/qr/challenge` | Inicia el acceso mediante QR | No | Previsto |
| `POST` | `/api/mobile/auth/face/verify` | Verificación facial 1:1 para completar el acceso | Challenge válido | Previsto |
| `POST` | `/api/mobile/auth/login` · `/mfa/verify` | Inicio de sesión (con segundo factor si la cuenta tiene MFA) | No | Implementado |
| `POST` | `/api/mobile/auth/refresh` · `/logout` | Renueva o cierra la sesión (refresh token en el cuerpo) | No | Implementado |
| `POST` | `/api/mobile/auth/change-password` · `/revoke-sessions` | Contraseña y sesiones | Sí | Implementado |
| `GET` | `/api/mobile/auth/me` | Información de la cuenta autenticada | Sí | Implementado |

### Flujo previsto de acceso

```
App MAUI                          Mobile BFF
   │  POST /auth/qr/challenge          │
   │ ────────────────────────────────► │  valida el QR y crea un challenge temporal
   │ ◄──────────────────────────────── │  challengeId + expiración
   │                                   │
   │  POST /auth/face/verify           │
   │  (challengeId + captura facial)   │
   │ ────────────────────────────────► │  verificación 1:1 contra la referencia de la cuenta
   │ ◄──────────────────────────────── │  sesión / token (formato pendiente de ADR)
   │                                   │
   │  GET /me                          │
   │ ────────────────────────────────► │
```

---

### `POST /api/mobile/auth/qr/challenge`

Recibe el contenido del QR escaneado y crea un challenge de corta duración.

**Body (forma propuesta):**

```json
{
  "qrPayload": "<contenido del QR>"
}
```

**Respuesta `200 OK` (forma propuesta):**

```json
{
  "challengeId": "00000000-0000-0000-0000-000000000000",
  "expiresAt": "2026-01-01T00:05:00Z"
}
```

**Errores previstos:** `400` (QR inválido o vencido), `429` (demasiados intentos).

---

### `POST /api/mobile/auth/face/verify`

Verificación facial **1:1**: compara la captura con la referencia de la cuenta asociada al challenge. No identifica entre muchas personas.

**Body (forma propuesta):** `challengeId` y la captura facial. El formato de la imagen (JSON base64 o `multipart/form-data`) se definirá al implementar.

**Respuestas previstas:**

| Código | Cuándo |
|---|---|
| `200 OK` | Verificación correcta; devuelve la sesión/token |
| `400` | Challenge inválido o captura ilegible |
| `401` | La verificación facial no coincide |
| `410 Gone` | Challenge vencido |
| `429` | Demasiados intentos |

Las imágenes faciales no se registran en logs.

---

### `GET /api/mobile/auth/me`

Devuelve la información de la cuenta autenticada.

**Respuesta `200 OK`:**

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "userName": "…",
  "email": "…",
  "roles": ["…"],
  "permissions": ["…"]
}
```

Devuelve datos de la **cuenta** (`ApplicationUser`). Si la cuenta está vinculada a un paciente, la inclusión de datos clínicos se definirá aparte: cuenta y paciente son conceptos distintos.

**Errores previstos:** `401`.

---

## Paramedic Workflow

> **Implementados:** `POST /api/mobile/emergencies`, `GET /api/mobile/emergencies/{id}` (V1, ver
> [ADR-008](../adr/ADR-008-prehospital-emergency.md)), `POST /api/mobile/emergencies/{id}/identify/document`,
> `POST /api/mobile/emergencies/{id}/vital-signs`, `POST /api/mobile/emergencies/{id}/procedures`,
> `POST /api/mobile/emergencies/{id}/medication-administrations`, `POST /api/mobile/emergencies/{id}/notes`,
> los traslados (`POST …/transfers`, `…/transfers/{transferId}/start` y `…/cancel`), la identificación por huella
> (`…/identify/fingerprint/sessions` y `GET /api/mobile/devices/fingerprint-readers`), `GET /api/mobile/catalogs/procedures`,
> `GET /api/mobile/catalogs/medications` y `GET /api/mobile/catalogs/transfer-destinations`.
> **El resto siguen siendo contratos previstos** y pueden evolucionar mediante revisión arquitectónica.

- **Usuarios:** personal clínico autenticado: un `ClinicalStaff` activo, vinculado a la cuenta y con al menos una
  afiliación activa. El rol y sus permisos los define el Rol 3.
- **Autorización:** todos los endpoints de `/api/mobile/emergencies` exigen el permiso `Emergencies.Operate` (rol
  Paramedic; Administrator lo tiene por tener todos) para entrar al módulo, además del permiso clínico de cada acción
  (abrir: `Encounters.Create`; leer: `Encounters.Read`; identificar por documento o por huella, y los lectores de huella:
  `Patients.Read` + `Patients.Identify`;
  signos vitales: `VitalSigns.Write`; procedimientos y su catálogo: `Procedures.Write`; medicamentos administrados:
  `MedicationAdministrations.Write`; catálogo de medicamentos: `Medications.Read`; observaciones:
  `ClinicalNotes.Write`; traslado y sus destinos: `Transfers.Manage`). Nunca
  se comprueba un rol. Doctor, Nurse y Receptionist no tienen `Emergencies.Operate`: `403`.
- **Acceso al caso concreto:** una emergencia es un `ClinicalEncounter` de tipo `Prehospital` **sin clínica**
  (`clinicId = null`) mientras no llega al hospital. La clínica base del paramédico no es el lugar de la emergencia y
  no da acceso: solo la lee el personal con una asignación activa (`EncounterStaffAssignment`, función `Paramedic`), y
  quien la abre queda asignado. Otro paramédico, aunque comparta la clínica base: `403`.
- **Modelo:** una *emergencia* es un episodio de atención prehospitalaria. Empieza **sin paciente identificado** (NN,
  código `NN-…` generado por el servidor) y se vincula a un `Patient` después. Al llegar, el hospital la recibe
  (`POST /api/web/transfers/{id}/receive`) y la **misma** atención pasa a tener la clínica destino.
- **Registros clínicos:** signos vitales, procedimientos y medicamentos se **agregan** al episodio; no se editan ni se borran desde la app (las correcciones se definirán con el Rol 2).
- **Hora del evento:** la apertura lleva `startedAt` y cada registro lleva la hora en que ocurrió en campo (`recordedAt` en los signos vitales, `performedAt` en los procedimientos, `administeredAt` en los medicamentos, `occurredAt` en las observaciones), distinta de la hora en que llega al servidor, para soportar envío diferido.
- **Idempotencia:** implementada en la apertura, en la identificación por documento, en la sesión de huella y su
  confirmación, en los signos vitales, en los procedimientos, en los medicamentos administrados, en las observaciones y
  en la solicitud de traslado; los demás `POST` la usarán igual. Cada `POST` lleva un
  `clientRequestId` (`Guid` generado en el dispositivo una vez por operación y **reutilizado en cada reintento**). El
  servidor guarda un recibo por (usuario, operación, `clientRequestId`) con el hash SHA-256 del contenido lógico:
  - misma clave y mismo contenido → la **misma** respuesta (`200`, sin crear nada);
  - misma clave y contenido distinto → `409`; la app debe empezar de nuevo con otra clave;
  - dos envíos simultáneos con la misma clave → uno crea (`201`) y el otro devuelve lo mismo (`200`), nunca un duplicado.
- **Ubicación:** fuera de V1 (FUTURE). No se envía ni se acepta: un campo `location` se rechaza con `400`.

| Método | Ruta | Descripción | Estado |
|---|---|---|---|
| `POST` | `/api/mobile/emergencies` | Abre una emergencia | Implementado |
| `GET` | `/api/mobile/emergencies/{id}` | Datos básicos de la emergencia y su traslado activo | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/identify/document` | Identifica al paciente NN por cédula o pasaporte | Implementado |
| `GET` | `/api/mobile/devices/fingerprint-readers` | Lectores de huella activos | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/identify/fingerprint/sessions` | Abre una lectura de huella con un lector | Implementado |
| `GET` | `/api/mobile/emergencies/{id}/identify/fingerprint/sessions/{sessionId}` | Estado de la lectura (y el candidato) | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/identify/fingerprint/sessions/{sessionId}/confirm` | Confirma el candidato e identifica | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/identify/fingerprint/sessions/{sessionId}/cancel` | Cancela la lectura | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/vital-signs` | Registra una toma de signos vitales | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/procedures` | Registra un procedimiento realizado | Implementado |
| `GET` | `/api/mobile/catalogs/procedures` | Catálogo fijo de procedimientos prehospitalarios | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/medication-administrations` | Registra un medicamento administrado (sin prescripción) | Implementado |
| `GET` | `/api/mobile/catalogs/medications` | Medicamentos activos del catálogo | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/notes` | Registra una observación (nota `Prehospital`) | Implementado |
| `GET` | `/api/mobile/catalogs/transfer-destinations` | Clínicas activas de destino | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/transfers` | Solicita el traslado a una clínica | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/transfers/{transferId}/start` | Inicia ese traslado (en camino) | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/transfers/{transferId}/cancel` | Cancela ese traslado | Implementado |

### Flujo previsto

```
POST /emergencies                  abre el episodio (paciente aún desconocido)
POST /emergencies/{id}/identify/document   identifica al paciente por documento (implementado)
POST /emergencies/{id}/identify/fingerprint/sessions   lectura de huella (implementado): el lector compara, el
                                   paramédico confirma (…/{sessionId}/confirm)
POST /emergencies/{id}/vital-signs ┐ (signos vitales: implementado)
POST /emergencies/{id}/procedures  ├ registros durante la atención (0..n veces; procedimientos: implementado)
POST /emergencies/{id}/medication-administrations │ (medicamentos: implementado)
POST /emergencies/{id}/notes       ┘ (observaciones: implementado)
POST /emergencies/{id}/transfers   solicita el traslado (implementado)
POST /emergencies/{id}/transfers/{transferId}/start   inicia el traslado; se sigue registrando en camino
POST /api/web/transfers/{id}/receive   el hospital lo recibe (Web BFF): fin de la atención prehospitalaria
```

---

### `POST /api/mobile/emergencies` — Implementado

Permisos: `Emergencies.Operate` + `Encounters.Create`. Crea en **una sola transacción** (todo o nada):

- la atención `Prehospital` sin clínica y con paciente NN;
- la asignación del paramédico autenticado (`Paramedic`);
- el registro de auditoría;
- el recibo de idempotencia.

**Body:**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "startedAt": "2026-01-01T00:00:00Z",
  "chiefComplaint": "Dolor torácico"
}
```

- `clientRequestId`: obligatorio, distinto de `Guid.Empty`. Se conserva en los reintentos.
- `startedAt`: obligatorio. Es un **instante**: ISO-8601 `yyyy-MM-ddTHH:mm:ss[.fffffff]` seguido de `Z` o de un
  offset explícito (`2026-10-23T15:30:00Z`, `2026-10-23T15:30:00.123Z`, `2026-10-23T11:30:00-04:00`,
  `2026-10-23T11:30:00.123-04:00`). El servidor lo normaliza a UTC antes de validar, calcular el hash y guardar.
  - **Sin offset** (`2026-10-23T15:30:00`) el instante es ambiguo: `400`. Nunca se interpreta con la zona horaria del
    servidor ni se supone UTC. También `400` para texto, fechas u offsets inválidos.
  - Se acepta desde **24 horas antes** hasta **5 minutos después** de la hora del servidor, ambos límites incluidos;
    fuera de ese rango, `400` (`field: startedAt`). Un reintento de una apertura ya registrada se responde aunque
    `startedAt` haya quedado fuera del rango.
- `chiefComplaint`: obligatorio (máx. 4000 caracteres; se recortan los espacios).
- El hash de idempotencia usa `startedAt` (normalizado a UTC) y `chiefComplaint` (recortado): el mismo instante
  escrito con `Z` o con otro offset es el mismo contenido (`200`, no `409`).
- **No se aceptan** `location`, `clinicId`, `staffId`, `paramedicId`, `patientId` ni `temporaryPatientCode` (ni ningún
  otro campo): `400`. El servidor decide la clínica (ninguna), el personal (el usuario autenticado) y el código NN.

**Respuesta** (`201 Created` con cabecera `Location: /api/mobile/emergencies/{id}`, o `200 OK` en un reintento):

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "temporaryPatientCode": "NN-0123456789ABCDEF0123456789ABCDEF",
  "status": "Created",
  "startedAtUtc": "2026-01-01T00:00:00+00:00",
  "chiefComplaint": "Dolor torácico",
  "patient": null
}
```

| Estado | Cuándo |
|---|---|
| `201` | Primera apertura con ese `clientRequestId` |
| `200` | Reintento con el mismo `clientRequestId` y el mismo contenido: la misma emergencia, nada nuevo |
| `400` | Validación: campos que faltan o sobran, `startedAt` sin offset, inválido o fuera de rango, `clientRequestId` vacío |
| `401` | Sin token válido |
| `403` | Sin `Emergencies.Operate` o `Encounters.Create`; la cuenta no es `ClinicalStaff` activo o no tiene afiliación activa |
| `409` | El `clientRequestId` ya se usó con otro contenido |

---

### `GET /api/mobile/emergencies/{id}` — Implementado

Permisos: `Emergencies.Operate` + `Encounters.Read`. Mientras la emergencia no tenga clínica, además hace falta una
asignación activa del usuario a ella. Cuando el hospital la recibe, se aplica la regla de clínica de cualquier atención
(un paramédico de otra base deja de leerla).

Devuelve la misma forma que el `POST`; `patient` es `null` mientras la emergencia no esté identificada.
`activeTransfer` es el traslado `Requested` o `InTransit` de la emergencia (misma forma que la respuesta de
`POST …/transfers`) o `null`; los clientes que no lo conocen lo ignoran.

`status` es el estado de la atención: `Created`, `InProgress`, `Transferred`, `Received`, `InEvolution`, `Closed` o
`Cancelled`.

**Errores:** `401`, `403` (sin permiso o sin asignación), `404` (no existe o no es una emergencia prehospitalaria).

---

### `POST /api/mobile/emergencies/{id}/identify/document` — Implementado

Permisos: `Emergencies.Operate` + `Patients.Read` + `Patients.Identify`, y acceso a la emergencia (mientras no tenga
clínica, una asignación activa del usuario). Busca el paciente por documento y lo vincula a la **misma** emergencia: no
se crea otra atención, no se crea ningún paciente y no se copia nada. Es la identificación de cualquier atención NN
(`EncounterService.IdentifyPatientAsync`), con su auditoría y su control de concurrencia.

**Body:**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "documentType": "NationalId",
  "documentNumber": "001-1234567-8"
}
```

- `clientRequestId`: obligatorio, distinto de `Guid.Empty`. Propio de la identificación (no es el de la apertura): la
  app lo genera una vez por intento y lo conserva en los reintentos.
- `documentType`: `NationalId` (cédula) o `Passport`. Cualquier otro tipo (`Other`, `QrCode`, `InternalCode`, …), un
  número en lugar del nombre o un valor desconocido: `400` (`Other` no demuestra identidad).
- `documentNumber`: obligatorio (máx. 250 caracteres), tal como se escribió. El servidor lo normaliza con la regla del
  Domain (`PatientIdentifier.Normalize`: para cédula y pasaporte, solo letras y dígitos, en mayúsculas), así que
  `001-1234567-8` y `00112345678` son el mismo documento. Vacío tras normalizar: `400`.
- **Alcance de plataforma:** la coincidencia es exacta sobre identificadores **activos** con `issuer` vacío (el
  alcance SCI-PH). El índice único `(type, issuer, normalizedValue)` garantiza 0 o 1 paciente. Un paciente que solo
  tiene ese número con otro emisor (por ejemplo `issuer = "DO"`) **no se encuentra** (`404`). La app no envía emisor;
  un contrato con país o emisor explícito se definirá más adelante.
- No se aceptan otros campos (`issuer`, `patientId`, …): `400`.

**Respuesta `200 OK`:** la misma forma que `GET /api/mobile/emergencies/{id}`, con el paciente identificado. El
documento sale **enmascarado** (`primaryIdentifier`); el número enviado nunca se devuelve.

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "temporaryPatientCode": "NN-0123456789ABCDEF0123456789ABCDEF",
  "status": "Created",
  "startedAtUtc": "2026-01-01T00:00:00+00:00",
  "chiefComplaint": "Dolor torácico",
  "patient": {
    "id": "00000000-0000-0000-0000-000000000000",
    "displayName": "Ana Pérez",
    "birthDate": "1985-03-14",
    "biologicalSex": "Female",
    "identityStatus": "Unverified",
    "primaryIdentifier": { "type": "NationalId", "value": "*******5678" }
  }
}
```

- La emergencia conserva su `id`, su `temporaryPatientCode` (`NN-…`, como historia), su estado y su clínica (ninguna).
- Se registra **una** auditoría `Identify` (código NN y paciente; nunca el documento).

| Estado | Cuándo |
|---|---|
| `200` | Identificada; también al repetir el mismo `clientRequestId` con el mismo contenido (nada nuevo, sin otra auditoría) |
| `400` | Validación: tipo no admitido, número vacío o demasiado largo, `clientRequestId` vacío, campos que sobran |
| `401` | Sin token válido |
| `403` | Sin `Emergencies.Operate`, `Patients.Read` o `Patients.Identify`; sin asignación a la emergencia; la cuenta no es `ClinicalStaff` activo o no tiene afiliación activa. Se responde antes de buscar el documento |
| `404` | Ningún paciente tiene ese documento en el alcance de plataforma (la emergencia sigue NN), o la emergencia no existe o no es prehospitalaria |
| `409` | La emergencia ya está identificada (por otra solicitud, aunque sea el mismo paciente), ya fue recibida por el hospital (también para un paramédico afiliado a esa clínica), o el `clientRequestId` ya se usó con otro documento, tipo o emergencia |

- **Idempotencia:** operación `prehospital-emergency.identify-document.v1`, clave (usuario, operación,
  `clientRequestId`). El hash usa el id de la emergencia, el tipo y el número **normalizado**: otra forma de escribir el
  mismo documento es el mismo contenido. El recibo guarda solo el hash, nunca el documento. Dos envíos simultáneos con la
  misma clave identifican una sola vez y ambos responden `200`. Dos identificaciones simultáneas con pacientes distintos:
  una gana y la otra recibe `409` (control de concurrencia sobre `PatientId`); nunca queda un estado mixto.
- **No encontrado no guarda nada** (ni recibo, ni paciente, ni auditoría): si el paciente se registra después, el mismo
  intento lo encuentra.
- El documento no se registra en logs ni aparece en los errores.

---

### Identificación por huella — Implementado

Diseño en [ADR-009](../adr/ADR-009-fingerprint-identification.md). El paramédico elige un lector (ESP32 + R307) y abre una
**sesión** sobre la emergencia NN; el lector la recoge del Device API, compara la huella **en el sensor** y envía el
resultado; la app consulta la sesión y, si hay coincidencia, muestra el candidato. **Solo la confirmación del paramédico
identifica la emergencia**, con el caso de uso de identificación de siempre (misma atención, código NN conservado,
auditoría `Identify`, `IdentifiedByStaffId` = el paramédico). La app nunca envía ni recibe el id del paciente.

- Permisos: `Emergencies.Operate` + `Patients.Read` + `Patients.Identify`, personal clínico activo, y la regla de
  escritura prehospitalaria: el paramédico asignado y **solo antes de que el hospital reciba la emergencia** (después:
  `409` para el paramédico que participó, aunque esté afiliado a la clínica destino; `403` para cualquier otro).
- La sesión vence a los **90 s** (esperando al lector y esperando la confirmación). Una por emergencia activa
  (`Pending`/`Matched`) y una `Pending` por lector. Una sesión vencida se comporta como `Expired`.
- Una referencia biométrica es de **un** lector: un paciente enrolado en otro lector no se reconoce. Cualquier lector
  activo con el scope de huella puede elegirse (no hay relación lector–ambulancia).
- Sin coincidencia, con mala lectura, con el lector en error o vencida, la emergencia sigue NN: el documento y continuar
  como NN siguen disponibles.

#### `GET /api/mobile/devices/fingerprint-readers`

Lectores **activos** con el scope `Device.Fingerprint.Identify`, por nombre. Nunca la clave, su hash ni los scopes.
`lastSeenAtUtc` es el último contacto registrado, no un estado "en línea".

```json
[ { "id": "…", "code": "ESP32-AMB-01", "name": "Ambulancia 1", "lastSeenAtUtc": "2026-10-27T12:00:00+00:00" } ]
```

#### `POST /api/mobile/emergencies/{id}/identify/fingerprint/sessions`

```json
{ "clientRequestId": "…", "deviceId": "…" }
```

Crea en **una transacción** una sesión `Pending` (hora del servidor; vence a los 90 s), su auditoría `Create` y el recibo.

| Estado | Cuándo |
|---|---|
| `201` | Sesión abierta |
| `200` | El mismo `clientRequestId` con la misma emergencia y el mismo lector: la misma sesión (en su estado actual) |
| `400` | `clientRequestId` o `deviceId` vacíos o ausentes, o campos que sobran (`patientId`…) |
| `403` | Sin permisos, sin asignación, o la emergencia ya es de una clínica y el usuario no participó |
| `404` | La emergencia no existe o no es prehospitalaria; el lector no existe, está revocado o no tiene el scope de huella |
| `409` | Emergencia ya identificada, cancelada o cerrada; recibida por el hospital; ya tiene una sesión activa; el lector está ocupado con otra; o la clave se usó con otra emergencia o lector |

- **Idempotencia:** operación `prehospital-emergency.fingerprint-session.v1`; el hash usa la emergencia y el lector;
  el recibo apunta a la sesión. Dos aperturas simultáneas con la misma clave abren una sola sesión.

**Respuesta** (también la del `GET` y la de `cancel`):

```json
{
  "id": "…", "status": "Matched", "readerId": "…", "expiresAtUtc": "2026-10-27T12:01:30+00:00",
  "failure": null,
  "candidate": { "displayName": "Ana Pérez", "dateOfBirth": "1985-03-14" }
}
```

- `status`: `Pending`, `Matched`, `NoMatch`, `Failed`, `Expired`, `Cancelled` o `Confirmed`.
- `failure`: `LowQuality` o `SensorError` cuando `Failed`; si no, `null`.
- `candidate`: solo con `Matched`, mientras la referencia siga activa **y el lector de la sesión siga siendo un lector de
  huella válido** (activo, con `Device.Fingerprint.Identify`; se reevalúa en cada consulta); solo nombre y fecha de
  nacimiento (nunca el id del paciente, sus documentos ni la referencia biométrica). Un `Matched` sin candidato
  (referencia revocada o lector que ya no es válido; no se dice cuál) no se puede confirmar: la app lo cancela.

#### `GET /api/mobile/emergencies/{id}/identify/fingerprint/sessions/{sessionId}`

El estado de la sesión, **solo para el paramédico que la abrió** y en su emergencia (`404` para cualquier otra
combinación). Una sesión `Pending` o `Matched` vencida se cierra como `Expired` al leerla. La app consulta cada segundo
mientras está `Pending` y deja de hacerlo al cambiar de estado o al salir de la pantalla.

#### `POST /api/mobile/emergencies/{id}/identify/fingerprint/sessions/{sessionId}/confirm`

```json
{ "clientRequestId": "…" }
```

Sin paciente: el servidor lo toma de la referencia de la sesión. Comprueba de nuevo que la sesión sea de esa emergencia
y de ese paramédico, esté `Matched` y no vencida, que la referencia siga activa, que la emergencia siga NN y sin clínica,
y, como último paso antes de identificar, que el lector de la sesión siga activo con `Device.Fingerprint.Identify`
([ADR-009](../adr/ADR-009-fingerprint-identification.md), decisión 11).
En **una transacción**: la identificación de la emergencia (auditoría `Identify`), la sesión `Confirmed` y el recibo.

| Estado | Cuándo |
|---|---|
| `200` | La emergencia identificada (misma forma que `GET /api/mobile/emergencies/{id}`); también al repetir el mismo `clientRequestId` (sin otra auditoría) |
| `400` | `clientRequestId` vacío o ausente, campos que sobran (`patientId`…) |
| `403` / `404` | Como en la apertura; `404` también para una sesión de otra emergencia o de otro paramédico |
| `409` | La sesión no está `Matched` o venció; la referencia se revocó; el lector de la sesión ya no es válido (revocado, inexistente o sin scope; mensaje genérico, la sesión sigue `Matched`); la emergencia ya fue identificada (por documento u otra sesión) o recibida por el hospital; o la clave se usó con otra sesión |

- **Idempotencia:** operación `prehospital-emergency.fingerprint-confirm.v1`; el hash usa la emergencia y la sesión; el
  recibo apunta a la **emergencia** (como la identificación por documento), así que un reintento la devuelve, también
  si el lector se revocó después de esa confirmación. Una confirmación rechazada no deja recibo: su reintento se evalúa
  de nuevo.
- **Concurrencia:** contra otra identificación gana una sola (`PatientId` es token de concurrencia; la otra recibe
  `409` y nunca sobrescribe al paciente); contra la recepción del hospital o la cancelación, `409`. Dos confirmaciones
  simultáneas: una identificación y una auditoría.

#### `POST /api/mobile/emergencies/{id}/identify/fingerprint/sessions/{sessionId}/cancel`

Sin cuerpo. `Pending` o `Matched` → `Cancelled`, con una auditoría `Update` solo en esa transición (`200`). Si el resultado
del lector llega mientras tanto, se cancela desde el estado que ganó. Ya terminada sin identificar (`Cancelled`,
`Expired`, `NoMatch`, `Failed`): `200` con ese estado y sin escribir nada. `Confirmed`: `409`.

---

### `POST /api/mobile/emergencies/{id}/vital-signs` — Implementado

Permisos: `Emergencies.Operate` + `VitalSigns.Write`, y acceso a la emergencia (mientras no tenga clínica, una
asignación activa del usuario). Registra **una toma** de 1 a 7 signos vitales sobre la **misma** emergencia, NN o
identificada, en **una sola transacción** (todo o nada):

- una fila `VitalSign` por valor, todas con la misma hora (`recordedAt`) y el personal autenticado;
- una auditoría `Create` de `VitalSign` por valor;
- el paso de la atención de `Created` a `InProgress` en la primera toma (como el primer registro en el Web);
- el recibo de idempotencia.

Si algo falla (un valor fuera de rango, la hora, el estado, la concurrencia), no queda nada: ni valores, ni auditorías,
ni recibo.

**Body:**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "recordedAt": "2026-01-01T00:00:00Z",
  "heartRate": 88,
  "respiratoryRate": 18,
  "systolicPressure": 120,
  "diastolicPressure": 80,
  "oxygenSaturation": 97,
  "temperature": 37.5,
  "glasgow": 15
}
```

- `clientRequestId`: obligatorio, distinto de `Guid.Empty`. Propio de la toma (no es el de la apertura ni el de la
  identificación): la app lo genera una vez por toma y lo conserva en los reintentos.
- `recordedAt`: obligatorio, la hora de la toma en el dispositivo. ISO-8601 con `Z` o un offset explícito, como
  `startedAt`; sin offset, `400`. Se normaliza a UTC y no se modifica. Se acepta desde el **inicio de la emergencia**
  (`startedAtUtc`) hasta **5 minutos después** de la hora del servidor, ambos límites incluidos (el mismo margen de
  reloj del dispositivo que la apertura); fuera de ese rango, `400` (`field: recordedAt`). Ese margen es solo de esta
  operación móvil: los signos vitales del Web siguen rechazando horas futuras.
- Valores: todos opcionales, pero hace falta **al menos uno** (`400`). A lo sumo uno por tipo (campos fijos). Enteros,
  salvo la temperatura (hasta **2 decimales**; con más, `400` en lugar de redondear). Límites de plausibilidad del
  Domain (`VitalSignLimits`), ambos incluidos; no son umbrales clínicos y no hay reglas entre valores:

  | Campo | Tipo | Unidad (servidor) | Rango |
  |---|---|---|---|
  | `heartRate` | `HeartRate` | `/min` | 0–300 |
  | `respiratoryRate` | `RespiratoryRate` | `/min` | 0–100 |
  | `systolicPressure` | `SystolicBloodPressure` | `mm[Hg]` | 0–300 |
  | `diastolicPressure` | `DiastolicBloodPressure` | `mm[Hg]` | 0–250 |
  | `oxygenSaturation` | `OxygenSaturation` | `%` | 0–100 |
  | `temperature` | `Temperature` | `Cel` | 25–45 |
  | `glasgow` | `GlasgowComaScale` | `{score}` | 3–15 |

- **No se aceptan** `unit` ni ningún otro campo: `400`. Las unidades las pone el servidor.

**Respuesta** (`201 Created` la primera vez, sin `Location`: una toma no tiene recurso propio; `200 OK` en un
reintento). Se construye con los valores recibidos en forma canónica y las unidades del servidor, así que un reintento
responde exactamente lo mismo:

```json
{
  "encounterId": "00000000-0000-0000-0000-000000000000",
  "recordedAtUtc": "2026-01-01T00:00:00+00:00",
  "measurements": [
    { "type": "HeartRate", "value": 88, "unit": "/min" },
    { "type": "Temperature", "value": 37.5, "unit": "Cel" }
  ]
}
```

| Estado | Cuándo |
|---|---|
| `201` | Primera toma con ese `clientRequestId` |
| `200` | Reintento con el mismo `clientRequestId` y la misma toma: nada nuevo, sin otras auditorías |
| `400` | Hora sin offset, inválida o fuera de rango; toma vacía; valor fuera de rango, no entero o con más de 2 decimales; campos que sobran; `clientRequestId` vacío |
| `401` | Sin token válido |
| `403` | Sin `Emergencies.Operate` o `VitalSigns.Write` (Doctor, Nurse, Receptionist); sin asignación a la emergencia (otro paramédico) |
| `404` | La emergencia no existe o no es prehospitalaria |
| `409` | El `clientRequestId` ya se usó con otra toma o en otra emergencia; la emergencia ya no admite registros (`Closed`, `Cancelled`, o ya recibida por el hospital; en `Transferred` sin clínica sí los admite) o cambió durante la operación |

- **Idempotencia:** operación `prehospital-emergency.vital-signs.v1`, clave (usuario, operación, `clientRequestId`); el
  recibo apunta a la emergencia. El hash usa la emergencia, `recordedAt` en UTC y los pares tipo/valor presentes en un
  orden fijo y con su valor numérico (`37.5` y `37.50` son lo mismo; `Z` u otro offset del mismo instante, también).
  Dos envíos simultáneos de la misma toma la registran una sola vez (N valores y N auditorías, no 2N) y ambos responden
  sin error.
- Un reintento de una toma ya registrada se responde aunque la hora o el estado hayan cambiado después, pero el acceso
  se vuelve a comprobar: un recibo nunca salta la autorización.
- Dos tomas con distinto `clientRequestId` son dos tomas, aunque tengan la misma hora: los signos vitales son una serie
  temporal.
- Los valores no se registran en logs ni aparecen en los errores.

---

### `GET /api/mobile/catalogs/procedures` — Implementado

Permisos: `Emergencies.Operate` + `Procedures.Write` (el catálogo solo sirve para registrar un procedimiento y no tiene
datos de pacientes; no existe `Procedures.Read`), y ser personal clínico activo. Doctor, Nurse y Receptionist: `403`.

**Catálogo fijo controlado por el servidor**, propio del prototipo SCI-PH (sistema de códigos `SCI-PH-PREHOSP`,
[ADR-008](../adr/ADR-008-prehospital-emergency.md), decisión 8). No es texto libre ni, todavía, un catálogo
administrable: podrá sustituirse por un catálogo administrado o una terminología estándar. Los códigos son estables.

```json
[
  { "code": "IMMOBILIZATION", "name": "Inmovilización", "description": "Inmovilización de columna o de extremidades para el traslado." },
  { "code": "HEMORRHAGE-CONTROL", "name": "Control de hemorragia", "description": "Compresión directa, vendaje compresivo o torniquete." },
  { "code": "OXYGEN-THERAPY", "name": "Oxigenoterapia", "description": "Administración de oxígeno suplementario." },
  { "code": "PERIPHERAL-IV-ACCESS", "name": "Acceso venoso periférico", "description": "Canalización de una vía venosa periférica." },
  { "code": "CPR", "name": "Reanimación cardiopulmonar", "description": "Compresiones torácicas y ventilación." }
]
```

### `POST /api/mobile/emergencies/{id}/procedures` — Implementado

Permisos: `Emergencies.Operate` + `Procedures.Write`, y acceso a la emergencia (mientras no tenga clínica, una
asignación activa del usuario). Registra **un procedimiento ya realizado** sobre la **misma** emergencia, NN o
identificada, en **una sola transacción** (todo o nada):

- un `ClinicalProcedure` en estado `Completed` (`ClinicalProcedure.RecordCompleted`), con el nombre, la descripción, el
  sistema (`SCI-PH-PREHOSP`) y el código del catálogo, el personal autenticado y `performedAtUtc = completedAtUtc`;
- una auditoría `Create` de `ClinicalProcedure`;
- el paso de la atención de `Created` a `InProgress` si es el primer registro clínico;
- el recibo de idempotencia.

**Body:**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "procedureCode": "OXYGEN-THERAPY",
  "performedAt": "2026-01-01T00:00:00Z",
  "outcome": "SpO2 97 % tras 5 minutos"
}
```

- `clientRequestId`: obligatorio, distinto de `Guid.Empty`. Propio del procedimiento: la app lo genera una vez por
  procedimiento y lo conserva en los reintentos.
- `procedureCode`: obligatorio, un `code` del catálogo, con coincidencia exacta. Fuera del catálogo (o vacío): `400`
  (`field: procedureCode`).
- `performedAt`: obligatorio, el único instante del procedimiento ya completado. ISO-8601 con `Z` o un offset explícito;
  sin offset, `400`. Se normaliza a UTC. Misma ventana que los signos vitales: desde el **inicio de la emergencia**
  hasta **5 minutos después** de la hora del servidor, ambos límites incluidos; fuera, `400` (`field: performedAt`).
- `outcome`: opcional, el **resultado** del procedimiento escrito por el paramédico. Se recorta; vacío = sin resultado.
  Máximo 4000 caracteres; con más, `400` (`field: outcome`).
- **No se aceptan** `name`, `description`, `codeSystem`, `notes`, `staffId`, `patientId`, `clinicId`, `completedAt`,
  `status` ni ningún otro campo: `400`. Todo eso lo ponen el servidor, el catálogo y el Domain.

**Respuesta** (`201 Created` la primera vez, sin `Location`; `200 OK` en un reintento). Se construye con la petición
canónica y la entrada del catálogo, así que un reintento responde exactamente lo mismo:

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "encounterId": "00000000-0000-0000-0000-000000000000",
  "code": "OXYGEN-THERAPY",
  "name": "Oxigenoterapia",
  "performedAtUtc": "2026-01-01T00:00:00+00:00",
  "outcome": "SpO2 97 % tras 5 minutos"
}
```

| Estado | Cuándo |
|---|---|
| `201` | Primer procedimiento con ese `clientRequestId` |
| `200` | Reintento con el mismo `clientRequestId` y el mismo procedimiento: nada nuevo, sin otra auditoría |
| `400` | Código vacío o fuera del catálogo; hora sin offset, inválida o fuera de rango; `outcome` demasiado largo; campos que sobran; `clientRequestId` vacío |
| `401` | Sin token válido |
| `403` | Sin `Emergencies.Operate` (Doctor, Nurse, Receptionist) o sin `Procedures.Write`; sin asignación a la emergencia (otro paramédico) |
| `404` | La emergencia no existe o no es prehospitalaria |
| `409` | El `clientRequestId` ya se usó con otro procedimiento, hora, resultado o emergencia; la emergencia ya no admite registros (`Closed`, `Cancelled`, o ya recibida por el hospital; en `Transferred` sin clínica sí los admite) o cambió durante la operación (cierre, despacho del traslado o identificación concurrentes) |

- **Idempotencia:** operación `prehospital-emergency.procedure.v1`, clave (usuario, operación, `clientRequestId`); el
  recibo apunta al `ClinicalProcedure`. El hash usa la emergencia, el código, `performedAt` en UTC y el `outcome`
  recortado (`Z` u otro offset del mismo instante, o el resultado con otros espacios, son lo mismo). Dos envíos
  simultáneos del mismo procedimiento lo registran una sola vez y ambos responden sin error, con el mismo `id`.
- Un reintento se responde aunque la hora o el estado hayan cambiado después, pero el acceso se vuelve a comprobar.
- Dos procedimientos con distinto `clientRequestId` son dos procedimientos, aunque sean el mismo a la misma hora.
- El `outcome` no se registra en logs ni aparece en los errores.

---

### `GET /api/mobile/catalogs/medications` — Implementado

Permisos: `Emergencies.Operate` + `Medications.Read`, y ser personal clínico activo. Doctor, Nurse y Receptionist: `403`.

Los medicamentos **activos** del catálogo global (`Medication`), ordenados por nombre y luego por concentración. Un
medicamento inactivo no aparece. No se devuelve `status`.

```json
[
  {
    "id": "00000000-0000-0000-0000-000000000000",
    "code": "DEV-PARACETAMOL-500",
    "name": "Paracetamol",
    "presentation": "Tableta",
    "concentration": "500 mg",
    "defaultRoute": "Oral"
  }
]
```

- `id` es la identidad del medicamento (`code`, `presentation`, `concentration` y `defaultRoute` pueden ser `null`).
- `defaultRoute` es el nombre de `MedicationRoute`: solo una **sugerencia** para preseleccionar la vía en la app.

### `POST /api/mobile/emergencies/{id}/medication-administrations` — Implementado

Permisos: `Emergencies.Operate` + `MedicationAdministrations.Write`, y acceso a la emergencia (mientras no tenga
clínica, una asignación activa del usuario). Registra **una dosis ya administrada** sobre la **misma** emergencia, NN o
identificada, en **una sola transacción** (todo o nada):

- una `MedicationAdministration` (`MedicationAdministration.Record`) en estado `Completed`, **sin prescripción**
  (`prescriptionId` y `prescriptionDetailId` son `null`: el paramédico no prescribe y no hace falta `Prescriptions.Write`),
  con el personal autenticado como `administeredByStaffId`;
- una auditoría `Create` de `MedicationAdministration`;
- el paso de la atención de `Created` a `InProgress` si es el primer registro clínico;
- el recibo de idempotencia.

**Body:**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "medicationId": "00000000-0000-0000-0000-000000000000",
  "dose": 500,
  "doseUnit": "mg",
  "route": "Oral",
  "administeredAt": "2026-01-01T00:00:00Z",
  "notes": "Con agua"
}
```

- `clientRequestId`: obligatorio, distinto de `Guid.Empty`. Propio de la dosis: la app lo genera una vez por dosis y
  lo conserva en los reintentos.
- `medicationId`: obligatorio, un medicamento del catálogo. Inexistente: `404`. **Inactivo: `409`** (se comprueba en
  cada envío, porque puede desactivarse después de que la app cargó el catálogo).
- `dose`: número `> 0`, con **máximo 3 decimales** y **menor que 1 000 000 000** (la columna es `numeric(12,3)`): fuera de
  eso, `400` (`field: dose`), nunca un redondeo ni un desbordamiento en la base de datos. No hay reglas clínicas de dosis.
- `doseUnit`: obligatorio, **texto libre** de 1 a 24 caracteres tras recortar espacios (`400`, `field: doseUnit`). Se
  guarda tal como se escribió: no hay catálogo de unidades ni conversión. La lista de unidades de la app
  (mg, mcg, g, mL, UI, gotas, inhalaciones, Otra) es solo una ayuda de interfaz, no una regla.
- `route`: obligatorio, el **nombre exacto** de `MedicationRoute`: `Oral`, `Intravenous`, `Intramuscular`,
  `Subcutaneous`, `Inhalation`, `Topical`, `Sublingual`, `Rectal`, `Nasal`, `Ophthalmic`, `Otic`, `Transdermal` u
  `Other`. Un número u otro texto: `400`. No tiene que coincidir con `defaultRoute`.
- `administeredAt`: obligatorio. ISO-8601 con `Z` o un offset explícito; sin offset, `400`. Se normaliza a UTC. Misma
  ventana que los signos vitales y los procedimientos: desde el **inicio de la emergencia** hasta **5 minutos después**
  de la hora del servidor, ambos límites incluidos; fuera, `400` (`field: administeredAt`).
- `notes`: opcional. Se recorta; vacío = sin notas. Máximo 4000 caracteres (`400`, `field: notes`).
- **No se aceptan** `prescriptionId`, `prescriptionDetailId`, `medicationName`, `name`, `presentation`,
  `concentration`, `defaultRoute`, `staffId`, `patientId`, `clinicId`, `status`, `administeredAtUtc` ni ningún otro
  campo: `400`.

**Respuesta** (`201 Created` la primera vez, sin `Location`; `200 OK` en un reintento). Se construye con la petición
canónica y el nombre del medicamento, así que un reintento responde exactamente lo mismo:

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "encounterId": "00000000-0000-0000-0000-000000000000",
  "medicationId": "00000000-0000-0000-0000-000000000000",
  "medicationName": "Paracetamol",
  "dose": 500,
  "doseUnit": "mg",
  "route": "Oral",
  "administeredAtUtc": "2026-01-01T00:00:00+00:00",
  "notes": "Con agua"
}
```

| Estado | Cuándo |
|---|---|
| `201` | Primera dosis con ese `clientRequestId` |
| `200` | Reintento con el mismo `clientRequestId` y la misma dosis: nada nuevo, sin otra auditoría |
| `400` | Dosis `<= 0`, con más de 3 decimales o `>= 1 000 000 000`; unidad vacía o de más de 24; vía desconocida o numérica; hora sin offset, inválida o fuera de rango; notas de más de 4000; campos que sobran (incluida la prescripción); `clientRequestId` o `medicationId` vacíos |
| `401` | Sin token válido |
| `403` | Sin `Emergencies.Operate` (Doctor, Nurse, Receptionist) o sin `MedicationAdministrations.Write`; sin asignación a la emergencia (otro paramédico) |
| `404` | La emergencia no existe o no es prehospitalaria; el medicamento no existe |
| `409` | El medicamento está inactivo; el `clientRequestId` ya se usó con otro medicamento, dosis, unidad, vía, hora, notas o emergencia; la emergencia ya no admite registros (`Closed`, `Cancelled`, o ya recibida por el hospital; en `Transferred` sin clínica sí los admite) o cambió durante la operación (cierre, despacho del traslado o identificación concurrentes) |

- **Idempotencia:** operación `prehospital-emergency.medication-administration.v1`, clave (usuario, operación,
  `clientRequestId`); el recibo apunta a la `MedicationAdministration`. El hash usa la emergencia, el medicamento, la
  dosis en forma canónica (`500` y `500.0` son lo mismo), la unidad recortada, el nombre de la vía, `administeredAt` en
  UTC y las notas recortadas. Duplicar una dosis podría representar una sobredosis ficticia: dos envíos simultáneos de
  la misma dosis la registran una sola vez y ambos responden sin error, con el mismo `id`.
- Un reintento se responde aunque la hora, el estado o el medicamento (desactivado después) hayan cambiado: la dosis era
  válida cuando se registró. El acceso sí se vuelve a comprobar.
- Dos dosis con distinto `clientRequestId` son dos dosis, aunque sean iguales y a la misma hora.
- Las notas no se registran en logs ni aparecen en los errores.
- El camino Web (`POST /api/web/encounters/{id}/medication-administrations`) no cambia: sigue exigiendo la clínica.

---

### `POST /api/mobile/emergencies/{id}/notes` — Implementado

Permisos: `Emergencies.Operate` + `ClinicalNotes.Write`, y acceso a la emergencia (mientras no tenga clínica, una
asignación activa del usuario). Registra **una observación** del paramédico sobre la **misma** emergencia, NN o
identificada, en **una sola transacción** (todo o nada):

- una `ClinicalNote` (`ClinicalNote.Write`) de tipo **`Prehospital`, fijado por el servidor**, con el personal
  autenticado como autor;
- una auditoría `Create` de `ClinicalNote` (**sin el texto de la nota**);
- el paso de la atención de `Created` a `InProgress` si es el primer registro clínico;
- el recibo de idempotencia.

Las notas son **inmutables**: no se editan ni se borran; una corrección es otra nota.

**Body:**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "occurredAt": "2026-01-01T00:00:00Z",
  "content": "Paciente consciente, orientado, sin dolor torácico."
}
```

- `clientRequestId`: obligatorio, distinto de `Guid.Empty`. Propio de la nota: la app lo genera una vez por nota y lo
  conserva en los reintentos.
- `occurredAt`: obligatorio. ISO-8601 con `Z` o un offset explícito; sin offset, `400`. Se normaliza a UTC. Misma ventana
  que los demás registros: desde el **inicio de la emergencia** hasta **5 minutos después** de la hora del servidor,
  ambos límites incluidos; fuera, `400` (`field: occurredAt`). En la app es **automática y no editable**: la hora del
  primer intento de guardar.
- `content`: obligatorio. Se recorta; vacío o solo espacios, `400` (`field: content`). **Máximo 8000 caracteres** en el
  servidor (`400`); la app limita a **4000** como decisión de UX móvil, no como regla clínica. El texto no aparece en
  los errores ni en los logs.
- **No se aceptan** `type`, `noteType`, `authorStaffId`, `patientId`, `clinicId`, `createdAtUtc`, `encounterId` ni
  ningún otro campo: `400`.

**Respuesta** (`201 Created` la primera vez, sin `Location`; `200 OK` en un reintento). Se construye con la petición
canónica, así que un reintento responde exactamente lo mismo. No incluye el autor (siempre es quien llama) ni la hora de
creación:

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "encounterId": "00000000-0000-0000-0000-000000000000",
  "type": "Prehospital",
  "content": "Paciente consciente, orientado, sin dolor torácico.",
  "occurredAtUtc": "2026-01-01T00:00:00+00:00"
}
```

| Estado | Cuándo |
|---|---|
| `201` | Primera nota con ese `clientRequestId` |
| `200` | Reintento con el mismo `clientRequestId` y la misma nota: nada nuevo, sin otra auditoría |
| `400` | Texto vacío o de más de 8000 caracteres; hora sin offset, inválida o fuera de rango; campos que sobran; `clientRequestId` vacío |
| `401` | Sin token válido |
| `403` | Sin `Emergencies.Operate` (Doctor, Nurse, Receptionist) o sin `ClinicalNotes.Write`; sin asignación a la emergencia (otro paramédico) |
| `404` | La emergencia no existe o no es prehospitalaria |
| `409` | El `clientRequestId` ya se usó con otro texto, hora o emergencia; la emergencia ya no admite registros (`Closed`, `Cancelled`, o ya recibida por el hospital; en `Transferred` sin clínica sí los admite) o cambió durante la operación (cierre, despacho del traslado o identificación concurrentes) |

- **Idempotencia:** operación `prehospital-emergency.note.v1`, clave (usuario, operación, `clientRequestId`); el recibo
  apunta a la `ClinicalNote`. El hash usa la emergencia, `occurredAt` en UTC y el texto recortado (`Z` u otro offset del
  mismo instante, o el texto con otros espacios alrededor, son lo mismo). Una nota es inmutable, así que duplicarla
  sería un registro clínico falso: dos envíos simultáneos de la misma nota la registran una sola vez y ambos responden
  sin error, con el mismo `id`.
- Un reintento se responde aunque la hora o el estado hayan cambiado después, pero el acceso se vuelve a comprobar.
- Dos notas con distinto `clientRequestId` son dos notas, aunque tengan el mismo texto.
- `CreatedAtUtc` lo fija el Domain con su propio reloj; no forma parte del hash, de la respuesta ni de la comparación de
  un reintento.
- El camino Web (`POST /api/web/encounters/{id}/notes`) no cambia: sigue exigiendo la clínica y rechazando horas futuras.

---

### Traslado y entrega (handoff) — Implementado

El destino es una **clínica** (`Clinic`) activa de la red. El paramédico solicita, inicia y, si hace falta, cancela el
traslado desde la app; **la recepción la hace el hospital** con el Web BFF existente
(`GET /api/web/transfers/incoming`, `POST /api/web/transfers/{id}/receive`), sin cambios.

| Etapa | Atención (`status`, `clinicId`) | Traslado | Quién registra | Responsable |
|---|---|---|---|---|
| En escena | `Created` / `InProgress`, sin clínica | — | Paramédico asignado | Paramédico |
| Solicitado | `InProgress`, sin clínica | `Requested` | Paramédico asignado | Paramédico (el hospital lo ve entrante) |
| **En traslado** | **`Transferred`, sin clínica** | **`InTransit`** | **Paramédico asignado** | Paramédico (el hospital puede recibirlo) |
| Entregado | `Received`, clínica = destino | `Received` | Personal del destino (Web) | Hospital |

- **`Transferred` sin clínica es el transporte prehospitalario activo:** los signos vitales, procedimientos,
  medicamentos y observaciones del móvil se siguen aceptando en ese estado. El Web mantiene sus reglas (no registra en
  `Transferred`).
- **La recepción termina la responsabilidad prehospitalaria:** con la clínica asignada, **toda escritura móvil se
  rechaza**: `409` para el paramédico que participó (aunque también esté afiliado a la clínica destino) y `403` para
  cualquier otro usuario.
- No hay evento separado de "llegada física" todavía, y `Complete` del traslado queda pendiente: el flujo termina en
  `Received`.

#### `GET /api/mobile/catalogs/transfer-destinations`

Permisos: `Emergencies.Operate` + `Transfers.Manage`, y ser personal clínico activo (no `Clinics.Manage`). Todas las
clínicas **activas**, sin restringir por tipo, por nombre, municipio e id:

```json
[ { "id": "…", "name": "Hospital Central", "type": "Hospital", "municipality": "Distrito Nacional", "province": "Santo Domingo" } ]
```

#### `POST /api/mobile/emergencies/{id}/transfers`

Permisos: `Emergencies.Operate` + `Transfers.Manage`, y la regla de escritura prehospitalaria (asignación, sin clínica).
Crea en **una transacción** un `ClinicalTransfer` `Requested` **sin clínica de origen**, su auditoría `Create`, el paso
`Created -> InProgress` si la atención aún no empezó (la solicitud cuenta como inicio) y el recibo de idempotencia. La
hora (`requestedAtUtc`) es la del servidor.

```json
{ "clientRequestId": "…", "destinationClinicId": "…", "reason": "Trauma mayor", "notes": "…" }
```

- `destinationClinicId`: obligatorio. No existe: `404`. Inactiva: `409`.
- `reason`: opcional, hasta 500 caracteres. `notes`: opcional, hasta 4000. Ambos se recortan; vacío = sin valor.
- **No se aceptan** `originClinicId`, `requestedAtUtc`, `requestedByStaffId`, `sentAtUtc`, `receivedAtUtc`, `status`,
  `staffId`, `patientId`, `clinicId`, `ambulanceId` ni ningún otro campo: `400`.
- `409` si la atención ya está en traslado o cerrada, o si ya tiene un traslado activo (también lo impide un índice
  único).
- **Idempotencia:** `prehospital-emergency.transfer-request.v1`; el hash usa la emergencia, el destino, el motivo y las
  notas recortados; el recibo apunta al traslado. `201` la primera vez; `200` con el **mismo** traslado (en su estado
  actual) en un reintento; `409` si la clave llega con otros datos. Dos envíos simultáneos con la misma clave crean uno
  solo; con claves distintas, uno recibe `409`.

**Respuesta** (también `activeTransfer` del `GET` de la emergencia, y la de `start` y `cancel`):

```json
{
  "id": "…", "encounterId": "…", "status": "Requested",
  "destination": { "id": "…", "name": "Hospital Central", "type": "Hospital", "municipality": "Distrito Nacional", "province": "Santo Domingo" },
  "requestedAtUtc": "2026-01-01T00:00:00+00:00", "sentAtUtc": null, "cancelledAtUtc": null
}
```

#### `POST /api/mobile/emergencies/{id}/transfers/{transferId}/start`

Permisos: los mismos. Sin body. **Sobre ese traslado de esa emergencia** (otro: `404`). `Requested`:
`ClinicalTransfer.Dispatch` por el actor a la hora del servidor (`InTransit`) y la atención pasa a `Transferred`, con
su auditoría `Transfer`, en una transacción (`200`). `InTransit`: reintento, `200` con el mismo traslado y sin escribir
nada. `Cancelled`, `Received` o `Completed`: `409`.

#### `POST /api/mobile/emergencies/{id}/transfers/{transferId}/cancel`

Permisos: los mismos. Sin body. **Sobre ese traslado de esa emergencia** (otro: `404`), nunca "el activo": un reintento
tardío de la cancelación de un traslado anterior no cancela uno nuevo. `Requested`: solo el traslado pasa a `Cancelled`.
`InTransit`: además la atención vuelve de `Transferred` a `InProgress`. Ambos con auditoría `Transfer` y la hora del
servidor (`200`). `Cancelled`: reintento, `200` sin escribir nada. `Received` o `Completed`: `409`. Cambiar de destino =
cancelar y solicitar de nuevo.

- **Idempotencia de `start` y `cancel`:** por estado y por `transferId`, sin `clientRequestId`.
- **Concurrencia:** el estado del traslado y el `Status`/`PatientId` de la atención son tokens de concurrencia: un
  inicio, una cancelación, una recepción, una identificación o un registro simultáneos se resuelven con un ganador y un
  `409` para el otro, sin escrituras parciales.
- **Auditoría:** `Create` en la solicitud y `Transfer` (estados antes y después) en el inicio y la cancelación; la
  recepción conserva la auditoría del Web. Llevan el actor, la atención y el `PatientId` (o `null` si es NN).
