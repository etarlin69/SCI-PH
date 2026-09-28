# Contratos del Mobile BFF

> **Estado: contratos iniciales previstos. NO están implementados todavía.**
>
> Estos endpoints describen la intención acordada para empezar a trabajar en paralelo (app móvil y backend). Pueden evolucionar mediante **revisión arquitectónica**; cualquier cambio debe reflejarse en este documento dentro del mismo PR.

- **Cliente:** aplicación .NET MAUI (flujos **Patient** y **Paramedic**)
- **Proyecto:** `SCI-PH.Mobile.BFF`
- **Prefijo:** `/api/mobile`
- **Formato:** JSON. Los errores se devuelven como `application/problem+json` (ProblemDetails).
- **Autenticación:** el tipo de token y su emisión están pendientes de un ADR.

## Patient — contratos iniciales previstos

| Método | Ruta | Descripción | Autenticación | Estado |
|---|---|---|---|---|
| `POST` | `/api/mobile/auth/qr/challenge` | Inicia el acceso mediante QR | No | Previsto |
| `POST` | `/api/mobile/auth/face/verify` | Verificación facial 1:1 para completar el acceso | Challenge válido | Previsto |
| `GET` | `/api/mobile/me` | Información de la cuenta autenticada | Sí | Previsto |

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

### `GET /api/mobile/me`

Devuelve la información de la cuenta autenticada.

**Respuesta `200 OK` (forma propuesta):**

```json
{
  "userId": "00000000-0000-0000-0000-000000000000",
  "displayName": "…",
  "roles": ["…"]
}
```

Devuelve datos de la **cuenta** (`ApplicationUser`). Si la cuenta está vinculada a un paciente, la inclusión de datos clínicos se definirá aparte: cuenta y paciente son conceptos distintos.

**Errores previstos:** `401`.

---

## Paramedic Workflow — contratos iniciales previstos

> **ESTOS ENDPOINTS SON CONTRATOS PREVISTOS Y TODAVÍA NO ESTÁN IMPLEMENTADOS.**
> Pueden evolucionar mediante revisión arquitectónica.

- **Usuarios:** personal paramédico autenticado. El rol y sus permisos los define el Rol 3.
- **Modelo:** una *emergencia* es un episodio de atención prehospitalaria. Puede empezar **sin paciente identificado** y vincularse a un `Patient` después.
- **Registros clínicos:** signos vitales, procedimientos y medicamentos se **agregan** al episodio; no se editan ni se borran desde la app (las correcciones se definirán con el Rol 2).
- **Hora del evento:** cada registro lleva `recordedAt` (hora en que ocurrió en campo), distinta de la hora en que llega al servidor, para soportar envío diferido.
- **Idempotencia (propuesta):** los `POST` aceptan un `clientRequestId` (`Guid` generado en el dispositivo) para que un reintento sin conexión no duplique registros.

| Método | Ruta | Descripción | Estado |
|---|---|---|---|
| `POST` | `/api/mobile/emergencies` | Abre una emergencia | Previsto |
| `GET` | `/api/mobile/emergencies/{id}` | Detalle de la emergencia y sus registros | Previsto |
| `POST` | `/api/mobile/emergencies/{id}/identify` | Vincula la emergencia a un paciente | Previsto |
| `POST` | `/api/mobile/emergencies/{id}/vitals` | Registra signos vitales | Previsto |
| `POST` | `/api/mobile/emergencies/{id}/procedures` | Registra un procedimiento realizado | Previsto |
| `POST` | `/api/mobile/emergencies/{id}/medications` | Registra un medicamento administrado | Previsto |
| `POST` | `/api/mobile/emergencies/{id}/transfer` | Inicia el traslado a un establecimiento | Previsto |

### Flujo previsto

```
POST /emergencies                  abre el episodio (paciente aún desconocido)
POST /emergencies/{id}/identify    identifica al paciente (documento, huella vía Device API, etc.)
POST /emergencies/{id}/vitals      ┐
POST /emergencies/{id}/procedures  ├ registros durante la atención (0..n veces)
POST /emergencies/{id}/medications ┘
POST /emergencies/{id}/transfer    traslado → lo recibe el hospital en Web BFF
                                   (POST /api/web/transfers/{id}/receive)
```

---

### `POST /api/mobile/emergencies`

**Body (forma propuesta):**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "startedAt": "2026-01-01T00:00:00Z",
  "location": { "latitude": 0.0, "longitude": 0.0 },
  "chiefComplaint": "…"
}
```

**Respuestas previstas:** `201 Created` (con `id` y cabecera `Location`), `400`, `401`, `403`.

---

### `GET /api/mobile/emergencies/{id}`

**Respuesta `200 OK` (forma propuesta):**

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "status": "Open",
  "startedAt": "2026-01-01T00:00:00Z",
  "patient": null,
  "vitals": [],
  "procedures": [],
  "medications": [],
  "transfer": null
}
```

`patient` es `null` mientras la emergencia no esté identificada.

**Errores previstos:** `401`, `403`, `404`.

---

### `POST /api/mobile/emergencies/{id}/identify`

Vincula la emergencia con un paciente existente o registra una identificación provisional.

**Body (forma propuesta):**

```json
{
  "clientRequestId": "00000000-0000-0000-0000-000000000000",
  "method": "Document | Fingerprint | Manual",
  "patientId": "00000000-0000-0000-0000-000000000000",
  "document": { "type": "…", "number": "…" }
}
```

- `Fingerprint`: la identificación la resuelve `POST /api/device/fingerprint/identify` (Device API); aquí solo se vincula el resultado (`patientId`).
- Los campos exactos de `Manual` (paciente no identificable) se definirán con el Rol 2.

**Respuestas previstas:** `200 OK`, `400`, `401`, `403`, `404` (emergencia o paciente inexistente), `409` (ya identificada con otro paciente).

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
