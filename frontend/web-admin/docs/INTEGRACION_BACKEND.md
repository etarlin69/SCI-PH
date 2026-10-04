# Integración con el backend (Web.BFF)

Notas que antes estaban como `TODO` en el código, agrupadas por archivo. Cada servicio mock mantiene su firma: al conectar el BFF solo cambia el cuerpo de cada método.

## `src/features/audit/services/auditService.ts`

- `GET /api/audit?from=&to=&usuario=&accion=&resultado=&q=&page=&size=&sort=` con paginación en servidor; el registro real tiene millones de eventos.
- `GET /api/audit/{id}`
- `GET /api/audit?email={email}&size={limit}`
- `GET /api/audit/facets`
- `POST /api/audit/export { format, filters }` → descarga generada en servidor.
- eliminar; el backend audita cada operación por sí mismo.

## `src/features/auth/services/authService.ts`

- el Web.BFF gestiona la sesión con una cookie HttpOnly + SameSite=Strict. La SPA nunca guarda tokens: tras `verifyMfa` el BFF fija la cookie y `getSession()` llama a `GET /api/auth/session`. Aquí la sesión se guarda en `sessionStorage` solo para que el mock sobreviva a recargas.
- `GET /api/auth/session` (401 → null).
- `POST /api/auth/login { usuario, password, recordarEquipo }` → `{ challengeId }`.
- `POST /api/auth/mfa { challengeId, code, method }` → fija la cookie de sesión.
- `POST /api/auth/logout` (invalida la cookie).
- `POST /api/auth/password/forgot { email }`.
- `GET /api/auth/password/reset/{token}` → `{ status }`.
- `POST /api/auth/password/reset { token, password }`.
- `GET /api/auth/activation/{token}` → `{ status, email, nombre, rol }`.
- `POST /api/auth/activation { token, password }`.
- `POST /api/auth/email/confirm { token }`.
- `POST /api/auth/email/resend { email }`.
- `GET /api/me/profile`.
- `PUT /api/me/profile { nombre, telefono, preferencias }`.
- `POST /api/me/password { actual, nueva }`.

## `src/features/catalogs/services/catalogsService.ts`

- `GET /api/catalogs/{kind}?q=&estado=&page=&size=&sort=`
- `GET /api/catalogs/summary`
- `POST /api/catalogs/{kind}`
- `PUT /api/catalogs/{kind}/{id}`
- `DELETE /api/catalogs/{kind}/{id}`
- `PATCH /api/catalogs/{kind}/{id}/status`
- `POST /api/catalogs/diagnosticos/import?dryRun=true` (multipart).
- `POST /api/catalogs/diagnosticos/import`

## `src/features/clinics/services/clinicsService.ts`

- `GET /api/clinics?q=&provincia=&estado=&page=&size=&sort=`
- `GET /api/clinics/{id}`
- `POST /api/clinics`
- `PUT /api/clinics/{id}`
- `PATCH /api/clinics/{id}/status`
- `DELETE /api/clinics/{id}`
- `GET /api/clinics/{id}/services`
- `PATCH /api/clinics/{id}/services/{serviceId} { disponible }`
- `GET /api/clinics/{id}/staff`
- `GET /api/clinics/{id}/devices`

## `src/features/devices/services/devicesService.ts`

- lo genera el backend.
- `GET /api/devices?q=&tipo=&estado=&page=&size=&sort=`
- `GET /api/devices/{id}`
- `GET /api/devices/{id}/accesses?resultado=&page=&size=`
- `POST /api/devices` → `{ device, secret, qrPayload, expiresAt }`
- `DELETE /api/devices/{id}/enrollment`
- `POST /api/devices/{id}/rotate`
- `POST /api/devices/{id}/revoke`
- `DELETE /api/devices/{id}`

## `src/features/invitations/services/invitationsService.ts`

- `GET /api/invitations?q=&tipo=&estado=&page=&size=&sort=`
- `GET /api/invitations/{id}`
- `POST /api/invitations/patients`
- `POST /api/invitations/staff`
- `POST /api/invitations/{id}/reissue`
- `POST /api/invitations/{id}/revoke`

## `src/features/profile/services/profileService.ts`

- `GET/PUT /api/me/profile`, `POST /api/me/password`.

## `src/features/roles/services/rolesService.ts`

- `GET /api/roles`
- `GET /api/roles/{id}`
- `GET /api/permissions/modules`
- `POST /api/roles`
- `PATCH /api/roles/{id}`
- `PUT /api/roles/{id}/permissions` (el backend valida que C/U/D impliquen R).
- `DELETE /api/roles/{id}`

## `src/features/staff/services/staffService.ts`

- `GET /api/staff?q=&puesto=&clinicId=&estado=&page=&size=&sort=`
- `GET /api/staff/{id}`
- `POST /api/staff { ...input, access }` (transacción en backend).
- `PUT /api/staff/{id}`
- `DELETE /api/staff/{id}?suspendAccount=true`
- `GET /api/staff/{id}/assignments`
- `POST /api/staff/{id}/assignments`
- `GET /api/staff/{id}/licenses`
- `POST /api/staff/{id}/licenses` · `PUT /api/staff/{id}/licenses/{licenseId}`
- `GET /api/staff/{id}/history`

## `src/features/users/services/usersService.ts`

- `GET /api/users?q=&rol=&estado=&page=&size=&sort=`
- `GET /api/users/{id}`
- `POST /api/users` — el backend genera la contraseña temporal, la envía por email si `entrega === 'email'` y nunca la persiste en claro.
- `PATCH /api/users/{id}`
- `POST /api/users/{id}/suspend` · `POST /api/users/{id}/reactivate`
- `DELETE /api/users/{id}`
- `GET /api/users/{id}/sessions`
- `DELETE /api/users/{id}/sessions/{sessionId}`
- `DELETE /api/users/{id}/sessions?forcePasswordChange=`
- `GET /api/users/{id}/security`
- `POST /api/users/{id}/password-reset`
- `DELETE /api/users/{id}/mfa`

## `src/services/core/actor.ts`

- el backend obtiene el actor de la cookie de sesión; eliminar.

## `src/services/core/errors.ts`

- mapear aquí los Problem Details (RFC 7807) que devuelva el Web.BFF.

## `src/services/core/mock.ts`

- eliminar este archivo cuando todos los servicios llamen al Web.BFF.

## `src/services/index.ts`

- ` el endpoint del Web.BFF que la sustituye.

## `src/auth/AuthContext.tsx`

- `getSession()` pasará a ser asíncrono (`GET /api/auth/session`); mostrar un spinner de arranque hasta resolverlo. La SPA nunca almacena tokens: la sesión vive en una cookie HttpOnly.

## `src/data/mock/mockAuth.ts`

- eliminar; la autenticación la resuelve el Web.BFF (cookie HttpOnly).

## `src/features/auth/components/DemoCredentialsHint.tsx`

- eliminar este componente.

## `src/features/auth/pages/ForbiddenPage.tsx`

- el BFF registra el intento denegado en auditoría y devuelve su referencia.

## `src/features/auth/pages/MfaPage.tsx`

- enviar `trust` para emitir la cookie de "equipo de confianza" (30 días).

## `src/features/devices/qr.ts`

- si el backend devuelve el QR ya renderizado, este módulo puede eliminarse.

## `src/features/devices/types/devices.types.ts`

- el backend genera el secreto y solo guarda su hash.

## `src/features/users/components/TemporaryPasswordField.tsx`

- la genera el backend; en la entrega por email nunca llega al navegador.

## `src/features/users/types/users.types.ts`

- debe generarla el backend y nunca viajar en claro salvo en la respuesta de alta.

## `src/hooks/useAsync.ts`

- si se adopta TanStack Query, sustituir por `useQuery` con la misma forma.
