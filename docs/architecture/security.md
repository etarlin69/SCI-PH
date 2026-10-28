# Arquitectura de seguridad

Decisiones en [ADR-006](../adr/ADR-006-identity-security.md) (cuentas humanas) y
[ADR-007](../adr/ADR-007-device-authentication.md) (dispositivos).

## Componentes

```
SCI-PH.Application/
├── Abstractions/Security/  IIdentityService, ITokenService, IRefreshTokenStore, ISecurityAuditLog, ICurrentUser,
│                           IAuthenticatorService, IAccountTokenService, IUserDirectory, IAccountInvitationStore,
│                           IClinicalIdentityLinkService, IDeviceCredentialStore, IEmailSender, ISecureTokenGenerator,
│                           IPatientAccessAuthorizer, IMfaChallengeStore
├── Common/Security/        SecurityRoles, Permissions, RolePermissions, DeviceScopes, CustomClaimTypes, TokenUses,
│                           UrlTokens, enums (auditoría, revocación, invitaciones, dispositivos)
├── DTOs/Security/          peticiones y respuestas
└── Features/
    ├── Authentication/     AuthenticationService (login, MFA, refresh, logout, me, contraseña, sesiones), MfaManagementService
    ├── Accounts/           AccountInvitationService, AccountRecoveryService, UserAccountService, AccountEmails, AccountEmailOptions
    ├── Authorization/      PatientAccessAuthorizer
    └── Devices/            DeviceCredentialService, DeviceAuthenticationService

SCI-PH.Persistence/
├── Identity/Models/        ApplicationUser, RefreshToken, SecurityAuditEvent, AccountInvitation, DeviceCredential, MfaLoginChallenge
├── Identity/Services/      IdentityService, RefreshTokenStore, SecurityAuditLog, AuthenticatorService, MfaChallengeStore, AccountTokenService,
│                           UserDirectory, AccountInvitationStore, DeviceCredentialStore, ClinicalIdentityLinkService
├── Identity/Seed/          IdentitySeeder, IdentityBootstrapOptions, IdentityDevelopmentAccountSeeder (solo Development)
├── Configurations/Identity/
└── Migrations/Identity/    InitialIdentity, IdentitySecurityFoundation, IdentitySecurityCompletion, MfaChallengeReplayProtection

SCI-PH.Infrastructure/
├── Security/               JwtTokenService, JwtOptions(+Validator), SecureTokenGenerator, HttpContextCurrentUser,
│                           DeviceApiKeyAuthentication, PatientRecordAuthorization
├── Email/                  EmailOptions (+validador), SmtpEmailSender, PickupDirectoryEmailSender, DisabledEmailSender
└── Web/                    FrontendCors, SecurityRateLimiting, SecurityHeaders, OpenApiSecurity
```

## Flujos

**Login (Web)**
```
POST /api/web/auth/login  (X-SCIPH-CSRF: 1)
 └► AuthenticationService.LoginAsync ─► IIdentityService (contraseña, bloqueo, cuenta activa)
      ├ sin MFA ─► access token (JSON) + refresh token (cookie __Host-sciph-refresh) ─► 200 {mfaRequired:false, session}
      └ con MFA ─► challenge JWT 5 min (token_use=mfa_challenge) ─► 200 {mfaRequired:true, mfaChallenge}
POST /api/web/auth/mfa/verify {challengeToken, code | recoveryCode}
      ─► JWT válido (firma, iss, aud, exp, token_use=mfa_challenge)
      ─► reclamo atómico en MfaLoginChallenges (SHA-256 del jti, mismo usuario, no expirado, ConsumedAtUtc IS NULL)
      ─► verificar código ─┬ incorrecto ─► liberar reclamo ─► 401 (cuenta para bloqueo)
                           └ correcto   ─► challenge consumido ─► sesión + cookie
      replay, challenge expirado, de otro usuario o petición concurrente perdedora ─► 401
```
Mobile es igual, pero access y refresh token van en el cuerpo y no hay cookies ni cabecera CSRF.

**Refresh**
```
cookie (Web) o body (Mobile) ─► SHA-256 ─► búsqueda
  ├ inexistente / expirado / revocado ───────────► 401
  ├ ya rotado (reuso) ─► revocar familia ──────────► 401
  ├ cuenta inactiva ─► revocar familia ─────────────► 401
  └ válido ─► rotar (atómico, mismo FamilyId) ─► nuevo access + nuevo refresh ─► 200
```

**Invitación y activación**
```
Admin/Recepción ─► POST /api/web/admin/invitations/{patients|staff}
  └► verificar registro clínico (existe, sin cuenta) ─► token aleatorio ─► guardar hash ─► email con enlace
Invitado ─► POST /api/web/auth/activate-account {token, password}
  └► hash ─► invitación pendiente y vigente ─► reclamar (uso único) ─► cuenta inactiva (email confirmado, rol)
     ─► vincular Patient/ClinicalStaff.IdentityUserId ─► activar ─► completar
     (fallo antes del vínculo: se borra la cuenta y la invitación vuelve a pendiente)
```

**Confirmación de email y reset de contraseña**
```
POST forgot-password / resend-confirmation {email} ─► 202 genérico ─► (si la cuenta existe) token de Identity ─► email
POST reset-password {userId, token, newPassword}  ─► política ─► nueva contraseña ─► security stamp ─► desbloqueo ─► revocar sesiones
POST confirm-email  {userId, token}               ─► EmailConfirmed = true
```

**MFA**
```
POST mfa/setup ─► clave + otpauth URI
POST mfa/enable {code} ─► 10 códigos de recuperación ─► revoca sesiones
POST mfa/recovery-codes/regenerate {code} ─► nuevos códigos (los anteriores dejan de servir)
POST mfa/disable {code} ─► revoca sesiones
```

MFA challenges are persisted and single-use:
- **Duración:** 5 min (`Jwt:MfaChallengeLifetimeMinutes`).
- **Almacenamiento:** el `jti` es aleatorio, de 256 bits, y solo se guarda su SHA-256.
- **Consumo:** atómico, con un compare-and-set sobre `ConsumedAtUtc`, seguro con varias instancias. Con dos
  verificaciones concurrentes, exactamente una gana.
- **Login nuevo:** borra los challenges anteriores del usuario.
- **Limpieza futura:** un job que borre los expirados (ver ADR-006, decisión 8).

**Dispositivo**
```
Admin ─► POST /api/web/admin/devices {deviceCode, name, scopes} ─► clave (una sola vez)
ESP32 ─► HTTPS + X-Device-Id + X-Device-Key ─► DeviceApiKey ─► principal con device_scope ─► policy del scope
```

## Endpoints

| Ruta | Acceso | Notas |
|---|---|---|
| `POST /api/web/auth/login`, `/mfa/verify`, `/refresh`, `/logout` | Anónimo + CSRF | Cookie HttpOnly; rate limit (salvo logout) |
| `GET /api/web/auth/me`, `POST /change-password`, `/revoke-sessions` | Autenticado | |
| `POST /api/web/auth/activate-account` | Anónimo | Rate limit autenticación |
| `POST /api/web/auth/forgot-password`, `/reset-password`, `/resend-confirmation`, `/confirm-email` | Anónimo | Rate limit recuperación; respuestas genéricas |
| `POST /api/web/auth/mfa/setup`, `/enable`, `/disable`, `/recovery-codes/regenerate` | Autenticado | |
| `GET/POST/PUT /api/web/admin/users…` | `Users.Manage` | listar, ver, activar, desactivar, roles, revocar sesiones |
| `POST /api/web/admin/invitations/patients` | `Accounts.InvitePatient` | |
| `POST /api/web/admin/invitations/staff`, `/{id}/reissue`, `/{id}/revoke` | `Users.Manage` | |
| `GET/POST /api/web/admin/devices…` | `Devices.Manage` | crear, listar, rotar, revocar |
| `GET /api/web/admin/roles` | `Users.Manage` | matriz de roles |
| `GET/POST /api/web/admin/clinics…` | `Clinics.Manage` | listar, ver, alta, activar, desactivar (datos clínico-organizativos, no Identity) |
| `GET/POST /api/web/admin/staff…` | `ClinicalStaff.Manage` | listar, ver, alta, activar, desactivar, afiliaciones (abrir, finalizar); la cuenta del personal se crea con la invitación (`Users.Manage`) |
| `POST /api/mobile/auth/login`, `/mfa/verify`, `/refresh`, `/logout` | Anónimo | Tokens en el cuerpo |
| `GET /api/mobile/auth/me`, `POST /change-password`, `/revoke-sessions` | Autenticado | |
| `POST /api/device/heartbeat` | Dispositivo con `Device.Heartbeat` | |
| `GET /api/device/fingerprint/sessions/pending`, `POST …/{sessionId}/result` | Dispositivo con `Device.Fingerprint.Identify` | identificación por huella (ADR-009) |
| `GET /api/device/fingerprint/enrollments/pending`, `POST …/{enrollmentId}/result` | Dispositivo con `Device.Fingerprint.Enroll` | registro de huellas (ADR-010) |
| `GET /api/device/fingerprint/deletions/pending`, `POST …/{deletionId}/result` | Dispositivo con `Device.Fingerprint.Enroll` | borrado del slot de una huella revocada (ADR-011); el lector recibe solo el slot, nunca el paciente |
| `GET /api/web/fingerprint-enrollers`, `…/api/web/patients/{id}/fingerprints…` | `Biometrics.Manage` + personal clínico activo que vea al paciente | registro y revocación de huellas; sin atajo por rol |
| `POST /api/web/patients/{id}/faces/enroll`, `…/faces/{referenceId}/revoke` | `Biometrics.Manage` + personal clínico activo que vea al paciente | registro y revocación del rostro (ADR-012) |
| `POST /api/web/patients/{id}/faces/verify` | `Patients.Identify` + personal clínico activo que vea al paciente | verificación facial 1:1 asistida; no es autenticación (ADR-012) |

Errores (ProblemDetails): 400 validación o enlace inválido, 401 sin credenciales o credenciales inválidas
(siempre genérico), 403 sin permiso o CSRF rechazado, 409 conflicto (último administrador, duplicados),
429 límite de peticiones.

## Configuración

| Clave | Dónde | Notas |
|---|---|---|
| `Jwt:Issuer`, `Jwt:Audience`, lifetimes | `appsettings.json` | |
| `Jwt:SigningKey` | User Secrets / `Jwt__SigningKey` | ≥ 32 bytes aleatorios; sin ella Web y Mobile no arrancan |
| `Security:AllowedOrigins` | `appsettings` | Orígenes exactos del front web; HTTPS y obligatorio en Production |
| `AccountEmail:*Url`, `InvitationLifetimeHours` | `appsettings` | Páginas del front que reciben los tokens |
| `Email:Mode` | `appsettings` | `Smtp` (Production), `PickupDirectory` o `Disabled` |
| `Email:PickupDirectory` | User Secrets | Ruta absoluta fuera del repositorio (desarrollo) |
| `Smtp:Host`, `Port`, `UseSsl`, `UserName`, `FromAddress`, `FromName` | `appsettings` / variables | |
| `Smtp:Password` | User Secrets / `Smtp__Password` | Nunca en el repositorio |
| `RateLimiting:*` | `appsettings` | Límites por política |
| `DeviceAuthentication:RequireHttps` | `appsettings` | `false` solo en Development |
| `IdentityBootstrap:*` | User Secrets / variables | Primer administrador |
| `IdentityDevelopmentAccounts:*` | User Secrets / variables | Cuentas de prueba, una por rol excepto Administrator; solo Development (en otro entorno el Web BFF no arranca) |

## Reglas para el código nuevo

- Proteger endpoints con `[Authorize(Policy = Permissions.X.Y)]`; nunca comparar strings de roles.
- Datos de un paciente: `IAuthorizationService.AuthorizeAsync(User, patientId, PatientRecordRequirement.Read(...))`
  o `IPatientAccessAuthorizer`; nunca confiar en un `patientId` del cliente para un usuario Patient.
- Endpoints públicos sensibles: `[EnableRateLimiting(...)]` y respuestas que no revelen si una cuenta existe.
- No registrar contraseñas, tokens (refresh, JWT, invitación, reset, confirmación, challenge), claves (MFA,
  dispositivo), códigos de recuperación, cabeceras `Authorization` ni la contraseña SMTP.
- No usar `ExecuteUpdate`/`ExecuteDelete` ni SQL directo sobre `SecurityAuditEvents`.
