# Contratos del Web BFF

> **Estado: contratos iniciales previstos. NO están implementados todavía.**
>
> Estos endpoints describen la intención acordada para empezar a trabajar en paralelo (frontend y backend). Pueden evolucionar mediante **revisión arquitectónica**; cualquier cambio debe reflejarse en este documento dentro del mismo PR.

- **Cliente:** aplicación web React + TypeScript (áreas **Hospital** y **Administración**)
- **Proyecto:** `SCI-PH.Web.BFF`
- **Prefijo:** `/api/web`
- **Formato:** JSON. Los errores se devuelven como `application/problem+json` (ProblemDetails).
- **Autenticación:** JWT Bearer según [ADR-006](../adr/ADR-006-identity-security.md). El refresh token viaja solo en la cookie HttpOnly `__Host-sciph-refresh`; login, `mfa/verify`, refresh y logout exigen la cabecera `X-SCIPH-CSRF: 1`. Todos los endpoints requieren un usuario autenticado salvo los de sesión y recuperación de cuenta; los de `/api/web/admin` requieren además el permiso correspondiente. Endpoints en `/api/web/auth` ([detalle](../architecture/security.md#endpoints)).

## Web Hospital — contratos iniciales previstos

| Método | Ruta | Descripción | Estado |
|---|---|---|---|
| `GET` | `/api/web/transfers` | Lista los traslados de pacientes visibles para el usuario | Previsto |
| `POST` | `/api/web/transfers/{id}/receive` | Marca un traslado como recibido | Previsto |

---

### `GET /api/web/transfers`

Devuelve los traslados de pacientes que el usuario autenticado puede ver.

**Query (propuesta, opcional):** filtros por estado y paginación. Se definirán al implementar.

**Respuesta `200 OK` (forma propuesta):**

```json
[
  {
    "id": "00000000-0000-0000-0000-000000000000",
    "status": "InTransit",
    "createdAt": "2026-01-01T00:00:00Z"
  }
]
```

Los campos exactos del traslado y del paciente se definirán junto con el modelo del dominio clínico.

**Errores previstos:** `401`, `403`.

---

### `POST /api/web/transfers/{id}/receive`

Registra que el establecimiento del usuario recibió al paciente del traslado `{id}`.

**Parámetros de ruta:** `id` (`Guid`)

**Respuestas previstas:**

| Código | Cuándo |
|---|---|
| `200 OK` / `204 No Content` | Traslado marcado como recibido |
| `401` / `403` | No autenticado / sin permiso |
| `404` | El traslado no existe o no es visible para el usuario |
| `409 Conflict` | El traslado no está en un estado que permita recibirlo |

---

## Web Administration — contratos iniciales previstos

> Los endpoints marcados **Implementado** existen; el resto son contratos previstos que pueden evolucionar
> mediante revisión arquitectónica. Detalle de seguridad en [security.md](../architecture/security.md#endpoints).

- **Prefijo:** `/api/web/admin`
- **Autorización:** permiso específico por endpoint (`Users.Manage`, `Accounts.InvitePatient`, `Devices.Manage`, `Audit.Read`).
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
