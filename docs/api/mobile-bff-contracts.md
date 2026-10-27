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
> `POST /api/mobile/emergencies/{id}/medication-administrations`, `GET /api/mobile/catalogs/procedures` y
> `GET /api/mobile/catalogs/medications`.
> **El resto siguen siendo contratos previstos** y pueden evolucionar mediante revisión arquitectónica.

- **Usuarios:** personal clínico autenticado: un `ClinicalStaff` activo, vinculado a la cuenta y con al menos una
  afiliación activa. El rol y sus permisos los define el Rol 3.
- **Autorización:** todos los endpoints de `/api/mobile/emergencies` exigen el permiso `Emergencies.Operate` (rol
  Paramedic; Administrator lo tiene por tener todos) para entrar al módulo, además del permiso clínico de cada acción
  (abrir: `Encounters.Create`; leer: `Encounters.Read`; identificar por documento: `Patients.Read` + `Patients.Identify`;
  signos vitales: `VitalSigns.Write`; procedimientos y su catálogo: `Procedures.Write`; medicamentos administrados:
  `MedicationAdministrations.Write`; catálogo de medicamentos: `Medications.Read`). Nunca
  se comprueba un rol. Doctor, Nurse y Receptionist no tienen `Emergencies.Operate`: `403`.
- **Acceso al caso concreto:** una emergencia es un `ClinicalEncounter` de tipo `Prehospital` **sin clínica**
  (`clinicId = null`) mientras no llega al hospital. La clínica base del paramédico no es el lugar de la emergencia y
  no da acceso: solo la lee el personal con una asignación activa (`EncounterStaffAssignment`, función `Paramedic`), y
  quien la abre queda asignado. Otro paramédico, aunque comparta la clínica base: `403`.
- **Modelo:** una *emergencia* es un episodio de atención prehospitalaria. Empieza **sin paciente identificado** (NN,
  código `NN-…` generado por el servidor) y se vincula a un `Patient` después. Al llegar, el hospital la recibe
  (`POST /api/web/transfers/{id}/receive`) y la **misma** atención pasa a tener la clínica destino.
- **Registros clínicos:** signos vitales, procedimientos y medicamentos se **agregan** al episodio; no se editan ni se borran desde la app (las correcciones se definirán con el Rol 2).
- **Hora del evento:** la apertura lleva `startedAt` y cada registro lleva la hora en que ocurrió en campo (`recordedAt` en los signos vitales, `performedAt` en los procedimientos, `administeredAt` en los medicamentos), distinta de la hora en que llega al servidor, para soportar envío diferido.
- **Idempotencia:** implementada en la apertura, en la identificación por documento, en los signos vitales, en los
  procedimientos y en los medicamentos administrados; los demás `POST` la usarán igual. Cada `POST` lleva un
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
| `POST` | `/api/mobile/emergencies/{id}/vital-signs` | Registra una toma de signos vitales | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/procedures` | Registra un procedimiento realizado | Implementado |
| `GET` | `/api/mobile/catalogs/procedures` | Catálogo fijo de procedimientos prehospitalarios | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/medication-administrations` | Registra un medicamento administrado (sin prescripción) | Implementado |
| `GET` | `/api/mobile/catalogs/medications` | Medicamentos activos del catálogo | Implementado |
| `POST` | `/api/mobile/emergencies/{id}/transfer` | Inicia el traslado a un establecimiento | Previsto |

### Flujo previsto

```
POST /emergencies                  abre el episodio (paciente aún desconocido)
POST /emergencies/{id}/identify/document   identifica al paciente por documento (implementado)
POST /emergencies/{id}/identify    identifica al paciente (huella vía Device API, manual)
POST /emergencies/{id}/vital-signs ┐ (signos vitales: implementado)
POST /emergencies/{id}/procedures  ├ registros durante la atención (0..n veces; procedimientos: implementado)
POST /emergencies/{id}/medication-administrations ┘ (medicamentos: implementado)
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
| `409` | El `clientRequestId` ya se usó con otra toma o en otra emergencia; la emergencia ya no admite registros (`Transferred`, `Closed`, `Cancelled`) o cambió durante la operación |

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
| `409` | El `clientRequestId` ya se usó con otro procedimiento, hora, resultado o emergencia; la emergencia ya no admite registros (`Transferred`, `Closed`, `Cancelled`) o cambió durante la operación (cierre, despacho del traslado o identificación concurrentes) |

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
| `409` | El medicamento está inactivo; el `clientRequestId` ya se usó con otro medicamento, dosis, unidad, vía, hora, notas o emergencia; la emergencia ya no admite registros (`Transferred`, `Closed`, `Cancelled`) o cambió durante la operación (cierre, despacho del traslado o identificación concurrentes) |

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
