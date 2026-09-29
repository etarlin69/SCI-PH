# Contratos del Web BFF

> **Estado: contratos iniciales previstos. NO están implementados todavía.**
>
> Estos endpoints describen la intención acordada para empezar a trabajar en paralelo (frontend y backend). Pueden evolucionar mediante **revisión arquitectónica**; cualquier cambio debe reflejarse en este documento dentro del mismo PR.

- **Cliente:** aplicación web React + TypeScript (áreas **Hospital** y **Administración**)
- **Proyecto:** `SCI-PH.Web.BFF`
- **Prefijo:** `/api/web`
- **Formato:** JSON. Los errores se devuelven como `application/problem+json` (ProblemDetails).
- **Autenticación:** pendiente de definir en un ADR. Todos los endpoints requerirán un usuario autenticado; los de `/api/web/admin` requerirán además un rol administrativo.

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

> **ESTOS ENDPOINTS SON CONTRATOS PREVISTOS Y TODAVÍA NO ESTÁN IMPLEMENTADOS.**
> Pueden evolucionar mediante revisión arquitectónica.

- **Prefijo:** `/api/web/admin`
- **Autorización:** usuario autenticado con rol administrativo. El catálogo de roles y permisos lo define el Rol 3.
- **Paginación (propuesta):** los listados aceptan `page` y `pageSize` y devuelven `{ "items": [...], "page": 1, "pageSize": 20, "total": 0 }`.

| Método | Ruta | Descripción | Estado |
|---|---|---|---|
| `GET` | `/api/web/admin/users` | Lista cuentas de usuario | Previsto |
| `GET` | `/api/web/admin/users/{id}` | Detalle de una cuenta | Previsto |
| `POST` | `/api/web/admin/users` | Crea una cuenta | Previsto |
| `PUT` | `/api/web/admin/users/{id}/roles` | Reemplaza los roles de una cuenta | Previsto |
| `GET` | `/api/web/admin/roles` | Lista los roles disponibles | Previsto |
| `GET` | `/api/web/admin/audit` | Consulta el registro de auditoría | Previsto |
| `GET` | `/api/web/admin/devices` | Lista los dispositivos ESP32 registrados | Previsto |

Estos endpoints gestionan **cuentas** (`ApplicationUser`, schema `identity`), no pacientes.

---

### `GET /api/web/admin/users`

**Query (propuesta):** `page`, `pageSize`, `search` (nombre o correo), `role`, `isActive`.

**Respuesta `200 OK` (forma propuesta):**

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000000",
      "userName": "…",
      "email": "…",
      "roles": ["…"],
      "isActive": true
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 0
}
```

**Errores previstos:** `401`, `403`.

---

### `GET /api/web/admin/users/{id}`

**Respuesta `200 OK`:** mismos campos que un elemento del listado, más datos de auditoría básicos (fecha de creación, último acceso) cuando estén disponibles.

**Errores previstos:** `401`, `403`, `404`.

---

### `POST /api/web/admin/users`

Crea una cuenta. La forma de entregar la credencial inicial (invitación, contraseña temporal u otra) la define el ADR de autenticación; **este contrato no incluye contraseñas**.

**Body (forma propuesta):**

```json
{
  "userName": "…",
  "email": "…",
  "roles": ["…"]
}
```

**Respuestas previstas:**

| Código | Cuándo |
|---|---|
| `201 Created` | Cuenta creada; cabecera `Location` con `/api/web/admin/users/{id}` |
| `400` | Datos inválidos (ProblemDetails de validación) |
| `401` / `403` | No autenticado / sin permiso |
| `409 Conflict` | Ya existe una cuenta con ese usuario o correo |

---

### `PUT /api/web/admin/users/{id}/roles`

Reemplaza el conjunto completo de roles de la cuenta.

**Body (forma propuesta):**

```json
{
  "roles": ["…"]
}
```

**Respuestas previstas:** `204 No Content`, `400` (rol inexistente), `401`, `403`, `404`.

Cada cambio de roles debe quedar registrado en la auditoría.

---

### `GET /api/web/admin/roles`

**Respuesta `200 OK` (forma propuesta):**

```json
[
  { "name": "…", "description": "…" }
]
```

**Errores previstos:** `401`, `403`.

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

Lista los dispositivos ESP32 + R307 que usan el Device API.

**Query (propuesta):** `page`, `pageSize`, `isOnline`.

**Respuesta `200 OK` (forma propuesta):**

```json
{
  "items": [
    {
      "deviceId": "…",
      "location": "…",
      "firmwareVersion": "0.0.0",
      "lastHeartbeatAt": "2026-01-01T00:00:00Z",
      "isOnline": true
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 0
}
```

`lastHeartbeatAt` proviene de `POST /api/device/heartbeat` (ver [device-api-contracts.md](device-api-contracts.md)). El alta y la revocación de dispositivos se documentarán junto con el ADR de autenticación de dispositivos.

**Errores previstos:** `401`, `403`.
