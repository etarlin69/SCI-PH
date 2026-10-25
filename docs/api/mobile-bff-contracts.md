# Contratos del Mobile BFF

> **Estado: contratos iniciales previstos, salvo los marcados como _Implementado_** (autenticación; apertura, lectura
> e identificación por documento de la emergencia prehospitalaria).
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
> [ADR-008](../adr/ADR-008-prehospital-emergency.md)) y `POST /api/mobile/emergencies/{id}/identify/document`.
> **El resto siguen siendo contratos previstos** y pueden evolucionar mediante revisión arquitectónica.

- **Usuarios:** personal clínico autenticado: un `ClinicalStaff` activo, vinculado a la cuenta y con al menos una
  afiliación activa. El rol y sus permisos los define el Rol 3.
- **Autorización:** todos los endpoints de `/api/mobile/emergencies` exigen el permiso `Emergencies.Operate` (rol
  Paramedic; Administrator lo tiene por tener todos) para entrar al módulo, además del permiso clínico de cada acción
  (abrir: `Encounters.Create`; leer: `Encounters.Read`; identificar por documento: `Patients.Read` + `Patients.Identify`;
  previsto: `VitalSigns.Write` para signos vitales, etc.). Nunca
  se comprueba un rol. Doctor, Nurse y Receptionist no tienen `Emergencies.Operate`: `403`.
- **Acceso al caso concreto:** una emergencia es un `ClinicalEncounter` de tipo `Prehospital` **sin clínica**
  (`clinicId = null`) mientras no llega al hospital. La clínica base del paramédico no es el lugar de la emergencia y
  no da acceso: solo la lee el personal con una asignación activa (`EncounterStaffAssignment`, función `Paramedic`), y
  quien la abre queda asignado. Otro paramédico, aunque comparta la clínica base: `403`.
- **Modelo:** una *emergencia* es un episodio de atención prehospitalaria. Empieza **sin paciente identificado** (NN,
  código `NN-…` generado por el servidor) y se vincula a un `Patient` después. Al llegar, el hospital la recibe
  (`POST /api/web/transfers/{id}/receive`) y la **misma** atención pasa a tener la clínica destino.
- **Registros clínicos:** signos vitales, procedimientos y medicamentos se **agregan** al episodio; no se editan ni se borran desde la app (las correcciones se definirán con el Rol 2).
- **Hora del evento:** la apertura lleva `startedAt` y cada registro llevará `recordedAt` (hora en que ocurrió en campo), distinta de la hora en que llega al servidor, para soportar envío diferido.
- **Idempotencia:** implementada en la apertura y en la identificación por documento; los demás `POST` la usarán
  igual. Cada `POST` lleva un
  `clientRequestId` (`Guid` generado en el dispositivo una vez por operación y **reutilizado en cada reintento**). El
  servidor guarda un recibo por (usuario, operación, `clientRequestId`) con el hash SHA-256 del contenido lógico:
  - misma clave y mismo contenido → la **misma** respuesta (`200`, sin crear nada);
  - misma clave y contenido distinto → `409`; la app debe empezar de nuevo con otra clave;
  - dos envíos simultáneos con la misma clave → uno crea (`201`) y el otro devuelve lo mismo (`200`), nunca un duplicado.
- **Ubicación:** fuera de V1 (FUTURE). No se envía ni se acepta: un campo `location` se rechaza con `400`.

| Método | Ruta | Descripción | Estado |
|---|---|---|---|
| `POST` | `/api/mobile/emergencies` | Abre una emergencia | Implementado |
| `GET` | `/api/mobile/emergencies/{id}` | Datos básicos de la emergencia | Implementado (sin registros todavía) |
| `POST` | `/api/mobile/emergencies/{id}/identify/document` | Identifica al paciente NN por cédula o pasaporte | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/identify` | Vincula la emergencia a un paciente (huella, manual) | Previsto |
| `POST` | `/api/mobile/emergencies/{id}/vitals` | Registra signos vitales | Previsto |
| `POST` | `/api/mobile/emergencies/{id}/procedures` | Registra un procedimiento realizado | Previsto |
| `POST` | `/api/mobile/emergencies/{id}/medications` | Registra un medicamento administrado | Previsto |
| `POST` | `/api/mobile/emergencies/{id}/transfer` | Inicia el traslado a un establecimiento | Previsto |

### Flujo previsto

```
POST /emergencies                  abre el episodio (paciente aún desconocido)
POST /emergencies/{id}/identify/document   identifica al paciente por documento (implementado)
POST /emergencies/{id}/identify    identifica al paciente (huella vía Device API, manual)
POST /emergencies/{id}/vitals      ┐
POST /emergencies/{id}/procedures  ├ registros durante la atención (0..n veces)
POST /emergencies/{id}/medications ┘
POST /emergencies/{id}/transfer    traslado → lo recibe el hospital en Web BFF
                                   (POST /api/web/transfers/{id}/receive)
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

Devuelve la misma forma que el `POST`; `patient` es `null` mientras la emergencia no esté identificada. Los registros
clínicos (signos vitales, procedimientos, medicamentos, traslado) se añadirán con sus endpoints.

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
| `409` | La emergencia ya está identificada (por otra solicitud, aunque sea el mismo paciente), o el `clientRequestId` ya se usó con otro documento, tipo o emergencia |

- **Idempotencia:** operación `prehospital-emergency.identify-document.v1`, clave (usuario, operación,
  `clientRequestId`). El hash usa el id de la emergencia, el tipo y el número **normalizado**: otra forma de escribir el
  mismo documento es el mismo contenido. El recibo guarda solo el hash, nunca el documento. Dos envíos simultáneos con la
  misma clave identifican una sola vez y ambos responden `200`. Dos identificaciones simultáneas con pacientes distintos:
  una gana y la otra recibe `409` (control de concurrencia sobre `PatientId`); nunca queda un estado mixto.
- **No encontrado no guarda nada** (ni recibo, ni paciente, ni auditoría): si el paciente se registra después, el mismo
  intento lo encuentra.
- El documento no se registra en logs ni aparece en los errores.

---

### `POST /api/mobile/emergencies/{id}/identify`

Vincula la emergencia con un paciente por huella o de forma manual. La identificación por documento tiene su propio
endpoint (`/identify/document`, arriba).

**Body (forma propuesta):**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "method": "Fingerprint | Manual",
  "patientId": "00000000-0000-0000-0000-000000000000"
}
```

- `Fingerprint`: la identificación la resuelve `POST /api/device/fingerprint/identify` (Device API); aquí solo se vincula el resultado (`patientId`).
- Los campos exactos de `Manual` (paciente no identificable) se definirán con el Rol 2.

**Respuestas previstas:** `200 OK`, `400`, `401`, `403`, `404` (emergencia o paciente inexistente), `409` (ya identificada).

---

### `POST /api/mobile/emergencies/{id}/vitals`

**Body (forma propuesta):**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "recordedAt": "2026-01-01T00:00:00Z",
  "heartRate": 0,
  "respiratoryRate": 0,
  "systolicPressure": 0,
  "diastolicPressure": 0,
  "oxygenSaturation": 0,
  "temperature": 0.0,
  "glasgow": 0
}
```

Todos los valores son opcionales individualmente; las unidades y rangos válidos se definirán con el Rol 2.

**Respuestas previstas:** `201 Created`, `400`, `401`, `403`, `404`, `409` (emergencia cerrada).

---

### `POST /api/mobile/emergencies/{id}/procedures`

**Body (forma propuesta):**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "recordedAt": "2026-01-01T00:00:00Z",
  "procedureCode": "…",
  "notes": "…"
}
```

`procedureCode` referenciará un catálogo que administrará el área de Administración (Rol 5).

**Respuestas previstas:** `201 Created`, `400`, `401`, `403`, `404`, `409`.

---

### `POST /api/mobile/emergencies/{id}/medications`

**Body (forma propuesta):**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "recordedAt": "2026-01-01T00:00:00Z",
  "medicationCode": "…",
  "dose": "…",
  "route": "…",
  "notes": "…"
}
```

Registra lo **administrado** por el paramédico. SCI-PH no prescribe.

**Respuestas previstas:** `201 Created`, `400`, `401`, `403`, `404`, `409`.

---

### `POST /api/mobile/emergencies/{id}/transfer`

Inicia el traslado del paciente a un establecimiento. El traslado aparece en `GET /api/web/transfers` del establecimiento destino.

**Body (forma propuesta):**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "destinationFacilityId": "00000000-0000-0000-0000-000000000000",
  "estimatedArrivalAt": "2026-01-01T00:30:00Z",
  "notes": "…"
}
```

**Respuestas previstas:** `201 Created`, `400`, `401`, `403`, `404`, `409` (ya existe un traslado activo).
