# Estructura de la solución

SCI-PH usa **Onion Architecture** sobre **ASP.NET Core .NET 9**. Todos los proyectos apuntan a `net9.0`.

```
SCI-PH.sln
├── src/
│   ├── Core/
│   │   ├── SCI-PH.Domain
│   │   └── SCI-PH.Application
│   ├── Infrastructure/
│   │   ├── SCI-PH.Persistence
│   │   └── SCI-PH.Infrastructure
│   └── Presentation/
│       ├── SCI-PH.Web.BFF
│       ├── SCI-PH.Mobile.BFF
│       └── SCI-PH.Device.API
├── tests/
│   ├── SCI-PH.Domain.Tests
│   ├── SCI-PH.Application.Tests
│   └── SCI-PH.IntegrationTests
├── tools/
│   └── SCI-PH.Device.Simulator   Consola que simula un dispositivo: heartbeats o lector de huella (local-containers.md)
└── docs/
```

## Proyectos

### SCI-PH.Domain (`src/Core`)

Núcleo del sistema. Contiene las entidades, value objects y reglas del dominio clínico (por ejemplo, `Patient`).

- No depende de ningún otro proyecto ni de frameworks (EF Core, ASP.NET Core, Azure, Identity).
- **No** contiene `ApplicationUser`: la identidad de autenticación no es un concepto del dominio clínico.

### SCI-PH.Application (`src/Core`)

Casos de uso y contratos que la aplicación necesita del exterior.

- Carpetas: `Abstractions/`, `Common/`, `DTOs/`, `Features/`, `Validators/`.
- `Abstractions/` define lo que Infrastructure y Persistence deben implementar:

  | Carpeta | Abstracción prevista |
  |---|---|
  | `AI` | `IAiAssistantService` |
  | `Biometrics` | `IFaceVerificationService` y `IFaceCaptureNormalizer` (implementadas, ADR-012), stores de huella (implementados), `IFingerprintService` (vacía) |
  | `Documents` | `IDocumentService` |
  | `OCR` | `IOcrService` |
  | `Persistence` | `IUnitOfWork` |
  | `QR` | `IQrService` |
  | `Security` | `IIdentityService`, `ITokenService`, `IRefreshTokenStore`, `ISecurityAuditLog`, `ICurrentUser` (implementadas) |
  | `Storage` | `IStorageService` |

  > Estado actual: son `public interface` **vacías**. Sus métodos se definirán junto con cada funcionalidad, mediante PR revisado.

- Seguridad: roles, permisos y matriz en `Common/Security/`; casos de uso en `Features/Authentication/` y `Features/Accounts/` ([detalle](security.md)).
- Registro de dependencias: `DependencyInjection.AddApplication()`, `AddAccountSecurity()` (Web y Mobile), `AddAccountManagement(configuration)` (Web) y `AddDeviceSecurity()` (Device API).
- Paquetes: solo `Microsoft.Extensions.DependencyInjection.Abstractions`.

### SCI-PH.Persistence (`src/Infrastructure`)

Acceso a datos con EF Core y PostgreSQL (Npgsql).

- `Contexts/IdentityDbContext.cs`: ASP.NET Core Identity, schema `identity`.
- `Contexts/ClinicalDbContext.cs`: datos clínicos, schema `clinical`.
- `Configurations/Clinical/`: una `IEntityTypeConfiguration<T>` por entidad clínica ([detalle](clinical-entities.md)).
- `Identity/`: `ApplicationUser` (cuenta, `IdentityUser<Guid>`), `RefreshToken`, `SecurityAuditEvent`, `AccountInvitation`, `DeviceCredential`, servicios sobre `UserManager` y los DbContexts, `IdentitySeeder` e `IdentityPolicy`.
- `Configurations/Identity/`: configuraciones EF de las entidades de Identity.
- `Migrations/Identity/`: migraciones de `IdentityDbContext` (`InitialIdentity`, `IdentitySecurityFoundation`, `IdentitySecurityCompletion`, `MfaChallengeReplayProtection`).
- `Migrations/Clinical/`: migraciones de `ClinicalDbContext` (existe `InitialClinical`).
- Registro de dependencias: `DependencyInjection.AddPersistence(configuration)`. Lee la cadena `ConnectionStrings:SciPhDatabase`.

### SCI-PH.Infrastructure (`src/Infrastructure`)

Implementaciones de servicios externos: Azure Blob Storage, verificación facial, OCR, QR, IA y otros.

- `Security/`: JWT, generador de secretos, `ICurrentUser`, esquema `DeviceApiKey` y autorización por recurso del paciente. `Email/`: SMTP y alternativas de desarrollo. `Web/`: CORS, rate limiting, cabeceras de seguridad y OpenAPI.
- Registro de dependencias: `DependencyInjection.AddInfrastructure(configuration)` (servicios externos, aún vacío) y `AddJwtSecurity`, `AddEmailDelivery`, `AddFrontendCors`, `AddSecurityRateLimiting` y `AddDeviceAuthentication` según el host.

### SCI-PH.Web.BFF, SCI-PH.Mobile.BFF, SCI-PH.Device.API (`src/Presentation`)

Composition roots. Cada uno es una aplicación ASP.NET Core independiente con la misma base:

- Controllers, ProblemDetails, Health Checks (`/health`) y OpenAPI (`/openapi/v1.json`, solo en Development).
- Registra `AddApplication()`, `AddPersistence(...)` y `AddInfrastructure(...)`.
- HTTP permitido en Development; HSTS y redirección HTTPS en los demás entornos.
- Cadena de conexión vía user-secrets.

| Proyecto | Cliente | Prefijo de rutas | Puerto HTTP local |
|---|---|---|---|
| Web.BFF | React + TypeScript | `/api/web` | 5074 |
| Mobile.BFF | .NET MAUI | `/api/mobile` | 5191 |
| Device.API | ESP32 + R307 | `/api/device` | 5139 |

## Pruebas (`tests/`)

| Proyecto | Referencia a | Propósito |
|---|---|---|
| SCI-PH.Domain.Tests | Domain | Pruebas unitarias del dominio |
| SCI-PH.Application.Tests | Application, Domain | Pruebas unitarias de casos de uso |
| SCI-PH.IntegrationTests | Application, Persistence, Infrastructure | Pruebas de integración |

Todas usan xUnit.

## Archivos de la raíz

| Archivo | Propósito |
|---|---|
| `global.json` | Fija el SDK 9.0.305 (`rollForward: latestFeature`) |
| `.editorconfig` | Estilo de código compartido |
| `docker-compose.yml` | Stack local: PostgreSQL, Azurite, migraciones, BFFs, Device API y web-admin ([detalle](local-containers.md)) |
| `.env.example` | Plantilla de variables para Docker (`.env` está ignorado) |
| `.github/workflows/build.yml` | CI: backend (restore, build, test, migraciones), frontend (lint, test, build) e imágenes Docker |
| `.github/pull_request_template.md` | Plantilla de PR |
