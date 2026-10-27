# Contratos del Device API

- **Cliente:** ESP32 con sensor de huella R307
- **Proyecto:** `SCI-PH.Device.API`
- **Prefijo:** `/api/device`
- **Formato:** JSON compacto (el ESP32 tiene memoria limitada). Los errores se devuelven como `application/problem+json` (ProblemDetails), con mensajes genéricos.
- **Autenticación:** de **dispositivo**, no de usuario ([ADR-007](../adr/ADR-007-device-authentication.md)): cabeceras `X-Device-Id` (código) y `X-Device-Key` (clave entregada una sola vez al registrar el dispositivo), solo sobre HTTPS y nunca en la URL. Cada endpoint exige un scope (`Device.Heartbeat`, `Device.Fingerprint.Identify`, `Device.Fingerprint.Enroll`). **El dispositivo se identifica solo por su credencial, nunca por un campo del cuerpo.** Ninguna credencial de dispositivo se guarda en el repositorio.
- **Ningún dato clínico al dispositivo:** el Device API nunca devuelve pacientes, ids de paciente, nombres, fechas de nacimiento, datos de la emergencia, registros clínicos, documentos ni historia.

## Endpoints

| Método | Ruta | Descripción | Scope | Estado |
|---|---|---|---|---|
| `POST` | `/api/device/heartbeat` | Señal periódica de que el dispositivo está activo | `Device.Heartbeat` | Implementado |
| `GET` | `/api/device/fingerprint/sessions/pending` | La captura que debe hacer este lector, si la hay | `Device.Fingerprint.Identify` | Implementado |
| `POST` | `/api/device/fingerprint/sessions/{sessionId}/result` | Lo que el sensor comparó para esa sesión | `Device.Fingerprint.Identify` | Implementado |
| `GET` | `/api/device/fingerprint/enrollments/pending` | El registro de huella que debe hacer este lector, si lo hay | `Device.Fingerprint.Enroll` | Implementado |
| `POST` | `/api/device/fingerprint/enrollments/{enrollmentId}/result` | Qué hizo el sensor al registrar la huella | `Device.Fingerprint.Enroll` | Implementado |

---

## Identificación por huella

Diseño en [ADR-009](../adr/ADR-009-fingerprint-identification.md):

- **La plantilla no sale del sensor.** El R307 guarda las plantillas en sus slots y **compara localmente**; el lector
  informa solo qué slot coincidió. SCI-PH no recibe ni guarda imágenes ni plantillas y no compara huellas: traduce
  (lector, slot) a una `PatientBiometricReference` de **ese** lector. Un paciente enrolado en otro lector no se reconoce.
- **Sesión:** el paramédico abre una sesión en la app (Mobile BFF) con el lector que elige. El lector **consulta** si
  tiene una sesión pendiente (modelo pull, sin push), captura y envía el resultado. La sesión vence a los **90 segundos**.
- **Correlación:** `sessionId` aleatorio, sesión ligada al lector autenticado, expiración corta, HTTPS y estado de un
  solo uso. No hay nonce.
- **La coincidencia no identifica:** el paramédico ve el candidato en la app y confirma; solo entonces el servidor
  identifica la emergencia. El dispositivo nunca identifica ni recibe permisos clínicos.
- Cada resultado queda como un `IdentificationAttempt` (lector, sesión, emergencia, paramédico, resultado), sin plantilla
  ni slot. La clave, el slot y la referencia nunca se registran en logs ni aparecen en errores.

Frecuencia de consulta recomendada: cada 1–2 s mientras el lector está activo (límite del Device API: 120 peticiones por
minuto por dispositivo e IP).

### `GET /api/device/fingerprint/sessions/pending`

La sesión `Pending` y no vencida de **este** lector (como máximo una).

**`200 OK`** (solo esto; nunca datos del paciente, de la emergencia ni el slot esperado):

```json
{ "sessionId": "00000000-0000-0000-0000-000000000000", "expiresAtUtc": "2026-10-27T12:01:30+00:00" }
```

**`204 No Content`:** no hay captura pendiente. `401`: credencial ausente, inválida, revocada o sin HTTPS. `403`: sin el
scope. `429`: límite de peticiones.

### `POST /api/device/fingerprint/sessions/{sessionId}/result`

```json
{ "outcome": "Match", "slot": 12 }
```

- `outcome` (texto): `Match`, `NoMatch`, `LowQuality` o `SensorError`.
- `slot`: entero no negativo, **obligatorio con `Match` y prohibido con los demás**. Su máximo depende del sensor y no se
  fija en el servidor.
- No se acepta ningún otro campo (`deviceId`, `template`, `patientId`…): `400`.

| Resultado del sensor | Sesión | Intento |
|---|---|---|
| `Match` con referencia activa de este lector en ese slot | `Matched` (espera la confirmación del paramédico) | Éxito, con la referencia y su paciente |
| `Match` de un slot sin referencia activa de este lector | `NoMatch` (el slot no es de fiar) | Fallo `NoMatch` |
| `NoMatch` | `NoMatch` | Fallo `NoMatch` |
| `LowQuality` | `Failed` | Fallo `LowQualityCapture` |
| `SensorError` | `Failed` | Fallo `ProviderUnavailable` |

| Estado | Cuándo |
|---|---|
| `204` | Resultado aceptado. También al **repetir exactamente el mismo resultado** (una respuesta perdida): no se crea otro intento ni cambia la sesión |
| `400` | Cuerpo inválido (resultado desconocido, slot ausente o sobrante, campos que sobran) |
| `401` / `403` | Credencial inválida o revocada / sin el scope |
| `404` | La sesión no existe **o es de otro lector** (no se distingue) |
| `409` | Ya hay **otro** resultado para la sesión, o la sesión venció, se canceló o se confirmó sin resultado previo |
| `429` | Límite de peticiones |

Dos resultados simultáneos para la misma sesión: uno se acepta y el otro recibe `409` (token de concurrencia sobre el
estado de la sesión); nunca hay dos intentos.

---

## Registro de huellas (enrolamiento)

Diseño en [ADR-010](../adr/ADR-010-fingerprint-enrollment.md). El personal del hospital abre un registro en el Web con un
lector que tiene `Device.Fingerprint.Enroll` y `Device.Fingerprint.Identify`; el lector lo recoge, guarda la plantilla
**en su sensor**, en un slot libre que **elige él**, e informa el slot. SCI-PH crea entonces la referencia del paciente
(`R307`, `r307:device:<id>:slot:<n>`). La plantilla nunca sale del sensor y el lector nunca recibe datos del paciente.

| Método | Ruta | Descripción | Scope | Estado |
|---|---|---|---|---|
| `GET` | `/api/device/fingerprint/enrollments/pending` | El registro que debe hacer este lector, si lo hay | `Device.Fingerprint.Enroll` | Implementado |
| `POST` | `/api/device/fingerprint/enrollments/{enrollmentId}/result` | Qué hizo el sensor para ese registro | `Device.Fingerprint.Enroll` | Implementado |

### `GET /api/device/fingerprint/enrollments/pending`

**`200 OK`:** `{ "enrollmentId": "…", "expiresAtUtc": "…" }` (solo esto; nunca paciente, nombre, fecha de nacimiento,
historia, referencia ni slot esperado). **`204`:** nada pendiente. `401` / `403` / `429` como en los demás endpoints.

### `POST /api/device/fingerprint/enrollments/{enrollmentId}/result`

```json
{ "outcome": "Enrolled", "slot": 3 }
```

- `outcome`: `Enrolled`, `Failed`, `LowQuality` o `SensorError`. `slot`: entero no negativo, obligatorio con `Enrolled` y
  prohibido con los demás (su rango depende del sensor y no se fija aquí). Ningún otro campo (`deviceId`, `template`,
  `patientId`…): `400`.

| Estado | Cuándo | Qué hace el lector |
|---|---|---|
| `204` | Aceptado; también al repetir **exactamente** el mismo resultado (respuesta perdida) | Conserva el slot |
| `400` | Cuerpo inválido | Borra el slot que escribió |
| `404` | El registro no existe o es de otro lector (no se distingue) | Borra el slot |
| `409` | El slot ya es una referencia activa de este lector (el registro sigue `Pending`: puede usar otro slot), el paciente ya tiene una huella activa en este lector, ya se aceptó **otro** resultado, o el registro venció o se canceló | Borra el slot |
| sin respuesta, timeout, 5xx, 429 | Incierto | **No** borra: reintenta el mismo resultado hasta que el registro venza; si vence sin 204, borra el slot |

Dos resultados simultáneos: uno se acepta y el otro recibe `409`; nunca dos referencias. El registro vence a los 120 s
(duración del prototipo; el tiempo real del R307 está pendiente de verificar).

---

### `POST /api/device/heartbeat`

Informa que el dispositivo está encendido y conectado.

Scope requerido: `Device.Heartbeat`. El dispositivo se identifica por su credencial, no por el cuerpo.

**Body:**

```json
{
  "firmwareVersion": "0.0.0",
  "uptimeSeconds": 0
}
```

**Respuestas:** `204 No Content` (recibido), `401` (credencial ausente, inválida, revocada o sin HTTPS),
`403` (sin el scope), `429` (límite de peticiones).

La frecuencia del heartbeat se acordará con el equipo de hardware.
