# Contratos del Device API

> **Estado: contratos iniciales previstos. NO están implementados todavía.**
>
> Estos endpoints describen la intención acordada para empezar a trabajar en paralelo (firmware y backend). Pueden evolucionar mediante **revisión arquitectónica**; cualquier cambio debe reflejarse en este documento dentro del mismo PR.

- **Cliente:** ESP32 con sensor de huella R307
- **Proyecto:** `SCI-PH.Device.API`
- **Prefijo:** `/api/device`
- **Formato:** JSON compacto (el ESP32 tiene memoria limitada). Los errores se devuelven como `application/problem+json` (ProblemDetails).
- **Autenticación:** de **dispositivo**, no de usuario ([ADR-007](../adr/ADR-007-device-authentication.md)): cabeceras `X-Device-Id` (código) y `X-Device-Key` (clave entregada una sola vez al registrar el dispositivo), solo sobre HTTPS y nunca en la URL. Cada endpoint exige un scope (`Device.Heartbeat`, `Device.Fingerprint.Identify`). Ninguna credencial de dispositivo se guarda en el repositorio.

## Endpoints previstos

| Método | Ruta | Descripción | Estado |
|---|---|---|---|
| `POST` | `/api/device/fingerprint/identify` | Identificación por huella en emergencia | Previsto |
| `POST` | `/api/device/heartbeat` | Señal periódica de que el dispositivo está activo | Implementado |

---

### `POST /api/device/fingerprint/identify`

Identifica a un paciente a partir de su huella en una **situación de emergencia**, cuando no puede identificarse por sí mismo.

**Body (forma propuesta):**

```json
{
  "deviceId": "<identificador del dispositivo>",
  "template": "<plantilla de huella codificada>"
}
```

El formato exacto de la plantilla del R307 y si la comparación se hace en el sensor o en el servidor se definirán junto con el equipo de hardware.

**Respuestas previstas:**

| Código | Cuándo |
|---|---|
| `200 OK` | Coincidencia encontrada; devuelve un identificador del paciente y datos mínimos de emergencia |
| `404 Not Found` | Sin coincidencia |
| `400` | Plantilla inválida |
| `401` / `403` | Dispositivo no autenticado o no autorizado |

Las plantillas de huella no se registran en logs. Cada identificación queda auditada (dispositivo, fecha y resultado).

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
