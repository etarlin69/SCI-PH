# ADR-006: Identidad, autenticación y autorización

- **Estado:** Propuesto para revisión en el PR de `feature/security-foundation`
- **Fecha:** 2026-10-02
- **Relacionado:** [ADR-003](ADR-003-postgresql-two-contexts.md), [ADR-005](ADR-005-clinical-model.md),
  [ADR-007](ADR-007-device-authentication.md), [arquitectura de seguridad](../architecture/security.md)

## Contexto

SCI-PH necesita cuentas para personal y pacientes en Web y Mobile, con sesiones revocables, permisos
finos, MFA, recuperación de cuenta y auditoría de seguridad, sin mezclar la identidad de acceso con los
datos clínicos ni con la identidad de los dispositivos.

## Decisiones

### 1. Cuenta ≠ paciente ≠ personal clínico

- `ApplicationUser` (`IdentityUser<Guid>`, schema `identity`) es solo una **cuenta de acceso**: agrega
  `CreatedAtUtc`, `LastLoginAtUtc`, `IsActive` y `DeactivatedAtUtc`; sin nombres, datos clínicos ni biometría.
- `Patient` y `ClinicalStaff` (schema `clinical`) referencian la cuenta con `Guid? IdentityUserId`, **sin FK
  entre schemas**. El único que escribe ese vínculo es `IClinicalIdentityLinkService` (Persistence), usando los
  métodos de dominio.

### 2. Reglas de cuenta

- Email único (`RequireUniqueEmail` + índice único `EmailIndex`). Contraseña ≥ 10 con mayúscula, minúscula,
  dígito y símbolo. Bloqueo: 5 fallos → 15 min (contraseña y segundo factor cuentan igual).
- Desactivar no borra: `IsActive = false` + `DeactivatedAtUtc`, renueva el security stamp y revoca sesiones.
- `RequireConfirmedEmail` sigue en `false`: las cuentas creadas por invitación nacen con email confirmado y el
  flujo de confirmación existe para cuentas de bootstrap o cambios futuros de email.

### 3. Alta de cuentas: invitaciones (sin registro público)

- **No existe registro público.** Un actor autorizado invita a un registro clínico existente:
  - Paciente: `Accounts.InvitePatient` (Administrador, Recepción). El email es el identificador Email
    principal activo del paciente, nunca uno enviado en la petición. Rol fijo `Patient`.
  - Personal: `Users.Manage` (Administrador). Rol limitado a Doctor, Nurse, Paramedic o Receptionist;
    `Administrator` y `Patient` nunca se otorgan por invitación (también lo impide `CK_AccountInvitations_Role`).
- `AccountInvitation` (schema `identity`) guarda tipo y id del sujeto (Guid sin FK), email, rol, **hash SHA-256**
  del token (32 bytes de `RandomNumberGenerator`), expiración (24 h configurable), estado y autor. El token
  crudo solo viaja en el email. Una sola invitación pendiente por sujeto: invitar de nuevo revoca la anterior.
- **Activación** (`POST /api/web/auth/activate-account`): el cliente presenta solo `token` + `password`. Sujeto,
  email y rol salen de la invitación, por lo que el token del paciente A nunca activa al paciente B ni permite
  cambiar de rol. Flujo: reclamar la invitación (compare-and-set sobre `Status`, uso único) → crear la cuenta
  **inactiva** con email confirmado y rol → vincular el registro clínico → activar → completar. Si algo falla
  antes del vínculo se compensa (se borra la cuenta, la invitación vuelve a pendiente o se revoca); un fallo
  posterior deja una cuenta inactiva y vinculada, inutilizable. Contraseña rechazada: la invitación sigue
  disponible.
- Si el email falla, la invitación queda creada con `EmailSent = false` y el administrador la reemite
  (`/reissue` revoca el token anterior y genera otro).

### 4. Email, confirmación y recuperación

- `IEmailSender` (Application) con implementaciones en Infrastructure: `Smtp` (MailKit, siempre TLS),
  `PickupDirectory` (archivos `.eml`, solo Development/Testing) y `Disabled` (no permitido en Production).
  La contraseña SMTP solo llega por User Secrets o variables de entorno. Plantillas de texto plano sin
  contraseñas.
- Tokens de confirmación y reset de ASP.NET Identity (Data Protection, ligados al security stamp). El key ring
  de Data Protection se guarda en `identity.DataProtectionKeys` para que los enlaces sobrevivan reinicios y
  funcionen en todos los hosts.
- `forgot-password` y `resend-confirmation` responden siempre `202` con el mismo cuerpo. Un reset exitoso
  renueva el security stamp, levanta el bloqueo y **revoca todas las sesiones**.

### 5. Roles, permisos y autorización por recurso

- Roles en `SecurityRoles`; permisos `Área.Acción` en `Permissions` (25), transportados como claims
  `permission`; matriz en `RolePermissions` (código + revisión, sincronizada por `IdentitySeeder`). No hay
  editor de permisos: los administradores solo asignan roles predefinidos.
- Cada permiso es una policy. Política por defecto: usuario autenticado.
- **Paciente**: ningún permiso genérico. `IPatientAccessAuthorizer` / `PatientRecordRequirement` permiten a un
  paciente **leer solo su propio registro** (`Patient.IdentityUserId == usuario actual`) para Patients, Encounters,
  Medications, Documents y Transfers en lectura; nunca escribir. El personal necesita el permiso.

| Permiso | Admin | Doctor | Nurse | Paramedic | Receptionist | Patient |
|---|:-:|:-:|:-:|:-:|:-:|:-:|
| Users.Manage, Audit.Read, Devices.Manage | ✔ | | | | | |
| Accounts.InvitePatient | ✔ | | | | ✔ | |
| Patients.Read | ✔ | ✔ | ✔ | ✔ | ✔ | propio* |
| Patients.Create, Patients.Update | ✔ | | | | ✔ | |
| Patients.Identify | ✔ | ✔ | | ✔ | ✔ | |
| Encounters.Read | ✔ | ✔ | ✔ | ✔ | | propio* |
| Encounters.Create | ✔ | ✔ | | ✔ | ✔ | |
| Encounters.Update | ✔ | ✔ | ✔ | ✔ | | |
| Encounters.Close | ✔ | ✔ | | | | |
| Triage/VitalSigns/ClinicalNotes/Procedures.Write | ✔ | ✔ | ✔ | ✔ | | |
| Diagnoses/Results/Prescriptions.Write | ✔ | ✔ | | | | |
| Medications.Read | ✔ | ✔ | ✔ | ✔ | | propio* |
| MedicationAdministrations.Write | ✔ | ✔ | ✔ | ✔ | | |
| Transfers.Read | ✔ | ✔ | ✔ | ✔ | ✔ | propio* |
| Transfers.Manage | ✔ | ✔ | | ✔ | ✔ | |
| Emergencies.Operate | ✔ | | | ✔ | | |
| Documents.Read | ✔ | ✔ | ✔ | | | propio* |
| Documents.Upload | ✔ | ✔ | ✔ | ✔ | ✔ | |

\* No es un permiso del token: se decide por recurso con `PatientRecordRequirement`.

### 6. Administración de cuentas

- `api/web/admin/users` (Users.Manage): listar, ver, activar, desactivar, asignar roles y revocar sesiones.
- Un administrador no puede quitarse su propio rol ni desactivarse, y el último administrador activo está
  protegido. `Patient` no se combina con roles de personal.
- Cambiar roles, desactivar, cambiar o resetear la contraseña y habilitar/deshabilitar MFA **revocan todas las
  sesiones** y renuevan el security stamp cuando Identity lo hace.

### 7. Tokens y sesiones

- **Access token**: JWT HS256 de 15 min con `sub`, `jti`, `token_use=access`, `name`, `email`, `role` y
  `permission`; nunca datos clínicos. JwtBearer valida emisor, audiencia (una por BFF), firma, algoritmo y
  vigencia (skew 30 s) y **rechaza cualquier token cuyo `token_use` no sea `access`**.
- **Refresh token**: 64 bytes aleatorios; solo se guarda su SHA-256. Rotación en cada uso con `FamilyId`,
  `ReplacedByTokenId` y `RevokedAtUtc` como token de concurrencia; reusar un token rotado revoca toda la familia.
- **Transporte Web**: el refresh token vive solo en la cookie `__Host-sciph-refresh` (`HttpOnly`, `Secure`,
  `SameSite=Strict`, `Path=/`, sin `Domain`, expiración = la del token). Nunca va en el JSON ni en
  `localStorage`/`sessionStorage`. `refresh` lee únicamente la cookie; `logout` revoca y la borra. El front web
  debe usar HTTPS también en desarrollo (perfil `https`) porque el prefijo `__Host-` exige `Secure`.
- **Transporte Mobile**: access y refresh token en el cuerpo, sin cookies, para guardarlos en MAUI
  SecureStorage.
- Tras logout, desactivación o cambio de permisos, el access token emitido sigue siendo válido hasta 15 min.
  No hay lista negra de JWT (decisión intencional).

### 8. MFA (TOTP)

- Autenticador TOTP de ASP.NET Identity (sin SMS). `setup` devuelve la clave compartida y el URI `otpauth://`
  (el QR lo genera el front; no confundir con el QR del paciente). `enable` exige un código válido y devuelve
  10 códigos de recuperación, que solo se muestran al habilitar o regenerar. `disable` y `regenerate` exigen
  código.
- Con MFA activo, la contraseña correcta solo produce un **challenge** JWT de 5 min con
  `token_use=mfa_challenge` (sin refresh, inválido en endpoints normales). `mfa/verify` acepta challenge +
  código TOTP o código de recuperación y entonces emite la sesión. Los fallos cuentan para el bloqueo.
- **MFA challenges are persisted and single-use.** Cada challenge produce como máximo una sesión:
  - **Identificador:** el `jti` del challenge es un secreto aleatorio de 256 bits (`RandomNumberGenerator`).
    En `identity."MfaLoginChallenges"` solo se guarda su **SHA-256** (hex, `UNIQUE`), junto con `UserId`,
    `CreatedAtUtc`, `ExpiresAtUtc` (= `exp` del JWT, 5 min) y `ConsumedAtUtc`. Nunca se guardan el JWT, el
    TOTP, el código de recuperación ni la clave del autenticador.
  - **Validación en `mfa/verify`:** firma, emisor, audiencia, `exp` y `token_use`; después el registro
    persistido debe existir, pertenecer al `sub` del token, no haber expirado y tener `ConsumedAtUtc` nulo.
  - **Consumo atómico, reclamar y luego verificar:** el challenge se reclama *antes* de comprobar el código con
    un compare-and-set (`ConsumedAtUtc` es token de concurrencia de EF, es decir,
    `UPDATE … WHERE "Id" = @id AND "ConsumedAtUtc" IS NULL`). Funciona entre varias instancias del backend sin
    locks, semáforos ni Redis.
    - Si el código es incorrecto, el reclamo se libera: el challenge sigue sirviendo hasta expirar y el fallo
      cuenta para el bloqueo.
    - Si el código es correcto (TOTP, o código de recuperación, que también se consume), el challenge queda
      consumido y se emite la sesión.
    - Como solo el ganador del reclamo verifica el código, no hay carrera entre "verificar" y "consumir".
  - **Replay:** el mismo challenge da 401 aunque no haya expirado, aunque se presente otro TOTP válido o el
    mismo TOTP dentro de su ventana, y aunque se use un código de recuperación. Un challenge firmado
    correctamente pero no emitido (o reasignado a otro usuario) también da 401.
  - **Concurrencia:** dos `mfa/verify` simultáneos con el mismo challenge y un código válido dan exactamente un
    200 y un 401. Mientras una petición tiene el challenge reclamado, una segunda petición concurrente recibe
    401 aunque su código sea correcto (falla cerrada).
  - **Un challenge vivo por usuario:** un login nuevo borra los challenges anteriores del usuario (activos o
    expirados), de modo que la tabla no crece sin límite.
  - **Limpieza futura:** las filas de usuarios que no vuelven a iniciar sesión quedan hasta el siguiente login.
    Más adelante, un job periódico podrá ejecutar `DELETE … WHERE "ExpiresAtUtc" < now() - interval '1 day'`,
    apoyado en el índice `(UserId, ExpiresAtUtc)`.
  - La FK a `AspNetUsers` es `Cascade` porque el challenge es estado efímero, no historial: el historial de
    seguridad vive en `SecurityAuditEvents`, que no tiene FK. Migración `MfaChallengeReplayProtection`.
- La clave del autenticador y los códigos de recuperación los gestiona Identity en `AspNetUserTokens`.

### 9. CSRF, CORS, rate limiting y cabeceras

- **CSRF** (solo endpoints que usan la cookie: login, mfa/verify, refresh, logout del Web BFF): `SameSite=Strict`,
  cabecera obligatoria `X-SCIPH-CSRF: 1` (un formulario o imagen de otro sitio no puede añadirla y un script
  de otro origen necesitaría un preflight que CORS rechaza) y `Origin`, si existe, debe ser un origen permitido
  o el propio host. El resto de endpoints usa `Authorization: Bearer`, que el navegador no envía solo. Se
  descartó ASP.NET Antiforgery: exigiría un endpoint y una cookie adicionales sin ventaja para una API pura.
- **CORS** (Web BFF): orígenes exactos desde `Security:AllowedOrigins`, con credenciales, cabeceras y métodos
  explícitos. Nunca `*`. En Production es obligatorio configurarlos y deben ser HTTPS. Mobile y Device no
  habilitan CORS.
- **Rate limiting** (ventana fija por IP): autenticación (login, refresh, mfa/verify, activate-account) 20/min;
  recuperación (forgot, reset, resend, confirm) 5/15 min; dispositivos 120/min por dispositivo + IP. Respuesta
  `429` ProblemDetails con `Retry-After`. Detrás de un proxy hay que configurar ForwardedHeaders.
- **Cabeceras**: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`;
  respuestas de autenticación con `Cache-Control: no-store`; HSTS y redirección HTTPS fuera de Development.
  Sin CSP (APIs JSON). Si un proxy añade las mismas cabeceras, se configuran en un solo lugar.

### 10. Auditoría de seguridad

- `identity.SecurityAuditEvents`, append-only, sin FK, con usuario afectado, **actor** (administrador) y
  dispositivo. 34 tipos de evento (login, refresh, reuso, sesiones, invitaciones, activación, email, reset,
  MFA, códigos de recuperación, roles, dispositivos). Nunca guarda contraseñas, tokens, claves, códigos ni
  cabeceras `Authorization`; un test recorre todos los flujos con logging Trace y verifica que ningún secreto
  aparezca en los logs.

### 11. Ubicación por capa

| Capa | Contenido |
|---|---|
| Application | Interfaces (`IIdentityService`, `ITokenService`, `IRefreshTokenStore`, `IAuthenticatorService`, `IMfaChallengeStore`, `IAccountTokenService`, `IUserDirectory`, `IAccountInvitationStore`, `IClinicalIdentityLinkService`, `IDeviceCredentialStore`, `IEmailSender`, `ISecureTokenGenerator`, `ICurrentUser`, `IPatientAccessAuthorizer`), DTOs, roles/permisos, casos de uso |
| Persistence | Entidades y configuraciones de Identity, implementaciones sobre `UserManager`/DbContexts, seeder, migraciones, Data Protection |
| Infrastructure | JWT, generador de secretos, email, CORS, rate limiting, cabeceras, OpenAPI, esquema de dispositivo, handler de autorización por recurso |
| Web / Mobile / Device | Controladores delgados y composición |

Persistence e Infrastructure referencian el framework ASP.NET Core (tokens de Identity y Data Protection;
autenticación, CORS y rate limiting). No se referencian entre sí.

### 12. Pruebas y CI

- Pruebas HTTP con los hosts reales de Web, Mobile y Device; pruebas de servicio sobre stores en memoria; y
  pruebas con **PostgreSQL 17 real** (Testcontainers): base nueva, upgrade desde `InitialIdentity` con una
  cuenta existente, constraints, aislamiento de schemas, regresión clínica y flujos completos.
- CI ejecuta build y tests en Release con `SCIPH_REQUIRE_DOCKER=true` (las pruebas de PostgreSQL no pueden
  saltarse) y `has-pending-model-changes` para ambos contextos con `dotnet-ef` 9.0.9 del manifiesto local.

## Límites conocidos

- Access token válido hasta 15 min tras revocar sesiones (sin lista negra).
- Identity no impide reutilizar un código TOTP dentro de su ventana. Para reutilizarlo hace falta un challenge
  nuevo, es decir, conocer también la contraseña: cada challenge es de un solo uso (decisión 8).
- `forgot-password` envía email solo si la cuenta existe: el tiempo de respuesta podría diferir; el rate limiting
  lo mitiga. Mover el envío a una cola lo eliminaría.
- El key ring de Data Protection y la clave del autenticador se guardan sin cifrado adicional en la base; en
  producción conviene protegerlos con Key Vault.
- La compensación de la activación no es una transacción distribuida entre schemas (ver decisión 3).
