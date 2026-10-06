# Integración con SCI-PH.Web.BFF

Fuente de verdad: el código del backend (`src/Presentation/SCI-PH.Web.BFF/Controllers`, `SCI-PH.Application/DTOs/Security`)
y `docs/api/web-bff-contracts.md`. Si este documento y el código difieren, gana el código.

## Sesión web (ADR-006)

| Elemento | Dónde vive | Quién lo maneja |
|---|---|---|
| Access token (JWT) | Memoria: `src/api/apiClient.ts` y `AuthProvider` | SPA: `Authorization: Bearer` |
| Refresh token | Cookie `__Host-sciph-refresh` (HttpOnly, Secure, SameSite=Strict) | Navegador; la SPA nunca la lee |
| Desafío MFA | Memoria del `AuthProvider` mientras dura `/mfa` | SPA |

- Todas las peticiones usan `credentials: 'include'`.
- `login`, `mfa/verify`, `refresh` y `logout` llevan `X-SCIPH-CSRF: 1` (CookieCsrfProtection) y van sin Bearer.
- Al arrancar (F5): `POST /refresh` → `WebSession` → `GET /me`. Mientras tanto `status = initializing` y no se
  renderiza contenido protegido.
- 401 en una petición autenticada → un único `POST /refresh` compartido (single-flight) → reintento una vez.
  Si el refresh falla, la sesión local termina (`unauthenticated`, aviso «sesión caducada»).
- 403 nunca cierra la sesión: la ruta muestra la página 403.
- Los errores son ProblemDetails (`src/api/problemDetails.ts`, `ApiError`); 429 lee `Retry-After`.

## Endpoints integrados

| Método | Ruta | Uso en la SPA |
|---|---|---|
| POST | `/api/web/auth/login` `{ identifier, password }` | `AuthProvider.login` → `{ mfaRequired, session, mfaChallenge }` |
| POST | `/api/web/auth/mfa/verify` `{ challengeToken, code \| recoveryCode }` | `/mfa` |
| POST | `/api/web/auth/refresh` | Restauración de sesión y refresh automático |
| POST | `/api/web/auth/logout` | Cerrar sesión |
| GET | `/api/web/auth/me` | Usuario, roles y permisos (`string[]`) |
| POST | `/api/web/auth/change-password` `{ currentPassword, newPassword }` | Perfil (cierra la sesión local después) |
| POST | `/api/web/auth/revoke-sessions` | Perfil · cerrar todas mis sesiones |
| POST | `/api/web/auth/forgot-password` `{ email }` | `/forgot-password` (202 genérico) |
| POST | `/api/web/auth/reset-password` `{ userId, token, newPassword }` | `/reset-password?userId=…&token=…` |
| POST | `/api/web/auth/activate-account` `{ token, password }` | `/activate-account?token=…` |
| POST | `/api/web/auth/confirm-email` `{ userId, token }` | `/confirm-email?userId=…&token=…` |
| POST | `/api/web/auth/resend-confirmation` `{ email }` | `/confirm-email` (enlace inválido) |
| POST | `/api/web/auth/mfa/setup` · `enable` · `disable` · `recovery-codes/regenerate` | Perfil · verificación en dos pasos |
| GET | `/api/web/admin/users?search&page&pageSize` · `/{id}` | Usuarios (`Users.Manage`) |
| POST/PUT | `/api/web/admin/users/{id}/activate` · `deactivate` · `revoke-sessions` · `roles` | Detalle de usuario |
| GET | `/api/web/admin/roles` | Roles y permisos, solo lectura |
| POST | `/api/web/admin/invitations/patients` `{ patientId }` | Invitaciones (`Accounts.InvitePatient`) |
| POST | `/api/web/admin/invitations/staff` `{ clinicalStaffId, email, role }` · `/{id}/reissue` · `/{id}/revoke` | Invitaciones (`Users.Manage`) |
| GET/POST | `/api/web/admin/devices` · `/{id}/rotate-secret` · `/{id}/revoke` | Dispositivos (`Devices.Manage`) |

Enums en JSON: el BFF no registra `JsonStringEnumConverter`, así que llegan como número
(`DeviceCredentialStatus`: 1 Active, 2 Revoked; `InvitationSubjectType`: 1 Patient, 2 ClinicalStaff).

## Pendiente de backend (la SPA no inventa estos endpoints)

| Necesidad | Pantalla afectada | Estado en la SPA |
|---|---|---|
| `GET /api/web/admin/audit` (previsto) | Auditoría | DEMO con datos ficticios |
| Listado y detalle de invitaciones | Invitaciones | Solo acciones; tabla de la sesión actual |
| Búsqueda de `Patient` y `ClinicalStaff` para invitar | Invitaciones | Se introduce el id |
| Clínicas, personal (ClinicalStaff admin), catálogos | Clínicas, Personal, Catálogos | DEMO |
| Agregados del dashboard | Dashboard | DEMO |
| Indicar si la cuenta tiene MFA en `/me` | Perfil | Se deduce con `mfa/setup` (409 = ya activa) |
| Permisos propios para clínicas/personal/catálogos | Menú y rutas DEMO | Provisionalmente `Users.Manage` |
