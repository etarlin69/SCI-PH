# SCI-PH

Backend de SCI-PH construido con **ASP.NET Core (.NET 9)** siguiendo **Onion Architecture**, con tres puntos de entrada (BFF / API) según el tipo de cliente.

> Estado: arquitectura base. Todavía no hay funcionalidades de negocio implementadas.

## Stack

| Área | Tecnología |
|---|---|
| Backend | ASP.NET Core .NET 9 |
| Arquitectura | Onion Architecture |
| Base de datos | PostgreSQL (una base física, dos `DbContext`, dos schemas) |
| Frontend web | React + TypeScript |
| Mobile | .NET MAUI |
| Hardware | ESP32 + sensor de huella R307 |
| Almacenamiento de archivos | Azure Blob Storage |
| IA | Asistente educativo (no diagnostica, no prescribe) |
| Biometría | Verificación facial 1:1 · Identificación por huella para emergencias |

## Estructura

```
SCI-PH.sln
src/
  Core/
    SCI-PH.Domain            Entidades y reglas del dominio. Sin dependencias.
    SCI-PH.Application       Casos de uso y abstracciones. Depende de Domain.
  Infrastructure/
    SCI-PH.Persistence       EF Core + PostgreSQL (IdentityDbContext, ClinicalDbContext).
    SCI-PH.Infrastructure    Servicios externos (Blob, OCR, QR, IA, biometría).
  Presentation/
    SCI-PH.Web.BFF           Backend for Frontend de la app React.
    SCI-PH.Mobile.BFF        Backend for Frontend de la app .NET MAUI.
    SCI-PH.Device.API        API para dispositivos ESP32.
tests/
  SCI-PH.Domain.Tests
  SCI-PH.Application.Tests
  SCI-PH.IntegrationTests
mobile/                      App .NET MAUI (solución propia: mobile/SCI-PH.Mobile.sln). Ver mobile/README.md.
docs/
```

Detalle en [docs/architecture/solution-structure.md](docs/architecture/solution-structure.md).

## Requisitos

- .NET SDK 9.0.305 o superior dentro de 9.0.x (ver [`global.json`](global.json))
- Docker (para PostgreSQL local)
- Herramientas locales (incluye `dotnet-ef` 9.0.9, alineado con EF Core): `dotnet tool restore`
- Docker también para las pruebas con PostgreSQL 17 (Testcontainers); sin Docker esas pruebas se omiten localmente

## Puesta en marcha local

1. **Base de datos**

   ```bash
   cp .env.example .env      # define tus propios valores; .env nunca se sube
   docker compose up -d
   docker compose ps         # postgres debe aparecer (healthy)
   ```

2. **Cadena de conexión** (una vez por proyecto; se guarda en user-secrets, fuera del repo)

   ```bash
   dotnet user-secrets set "ConnectionStrings:SciPhDatabase" "Host=localhost;Port=5432;Database=sciph_dev;Username=<usuario>;Password=<password>" --project src/Presentation/SCI-PH.Web.BFF
   dotnet user-secrets set "ConnectionStrings:SciPhDatabase" "Host=localhost;Port=5432;Database=sciph_dev;Username=<usuario>;Password=<password>" --project src/Presentation/SCI-PH.Mobile.BFF
   dotnet user-secrets set "ConnectionStrings:SciPhDatabase" "Host=localhost;Port=5432;Database=sciph_dev;Username=<usuario>;Password=<password>" --project src/Presentation/SCI-PH.Device.API
   ```

   Usa los mismos valores que definiste en `.env`.

   **Clave de firma JWT** (Web y Mobile BFF; sin ella no arrancan). Genera un valor aleatorio propio de al menos 32 bytes, por ejemplo con `openssl rand -base64 48`, y guárdalo solo en user-secrets:

   ```bash
   dotnet user-secrets set "Jwt:SigningKey" "<valor-aleatorio-generado-por-ti>" --project src/Presentation/SCI-PH.Web.BFF
   dotnet user-secrets set "Jwt:SigningKey" "<valor-aleatorio-generado-por-ti>" --project src/Presentation/SCI-PH.Mobile.BFF
   ```

   Roles, permisos, administrador inicial, email (SMTP o carpeta local `.eml` en desarrollo) y orígenes CORS: ver
   [Arquitectura de seguridad](docs/architecture/security.md#configuración). El front web usa la cookie `__Host-sciph-refresh`,
   que exige HTTPS: ejecuta el Web BFF con el perfil `https` cuando pruebes el front.

3. **Migraciones**

   ```bash
   dotnet ef database update --context IdentityDbContext --project src/Infrastructure/SCI-PH.Persistence --startup-project src/Presentation/SCI-PH.Web.BFF
   dotnet ef database update --context ClinicalDbContext --project src/Infrastructure/SCI-PH.Persistence --startup-project src/Presentation/SCI-PH.Web.BFF
   ```

   El modelo clínico está descrito en [Entidades clínicas](docs/architecture/clinical-entities.md) y la seguridad en [Arquitectura de seguridad](docs/architecture/security.md).

4. **Compilar, probar y ejecutar**

   ```bash
   dotnet build
   dotnet test
   dotnet run --project src/Presentation/SCI-PH.Web.BFF --launch-profile http
   ```

## Endpoints técnicos (perfil `http`, entorno Development)

| Aplicación | Health | OpenAPI |
|---|---|---|
| Web BFF | http://localhost:5074/health | http://localhost:5074/openapi/v1.json |
| Mobile BFF | http://localhost:5191/health | http://localhost:5191/openapi/v1.json |
| Device API | http://localhost:5139/health | http://localhost:5139/openapi/v1.json |

## Documentación

- Arquitectura
  - [Estructura de la solución](docs/architecture/solution-structure.md)
  - [Reglas de dependencia](docs/architecture/dependency-rules.md)
  - [Componentes en ejecución](docs/architecture/runtime-components.md)
- Decisiones (ADR)
  - [ADR-001 Onion Architecture](docs/adr/ADR-001-onion-architecture.md)
  - [ADR-002 Separación en BFFs](docs/adr/ADR-002-bff-separation.md)
  - [ADR-003 PostgreSQL con dos contextos](docs/adr/ADR-003-postgresql-two-contexts.md)
  - [ADR-004 Almacenamiento de documentos](docs/adr/ADR-004-document-storage.md)
  - [ADR-005 Modelo de datos clínico](docs/adr/ADR-005-clinical-model.md)
  - [ADR-006 Identidad y seguridad](docs/adr/ADR-006-identity-security.md)
  - [ADR-007 Autenticación de dispositivos](docs/adr/ADR-007-device-authentication.md)
- Contratos de API (previstos)
  - [Web BFF](docs/api/web-bff-contracts.md)
  - [Mobile BFF](docs/api/mobile-bff-contracts.md)
  - [Device API](docs/api/device-api-contracts.md)
- Equipo
  - [Responsabilidades por rol](docs/team/role-handoff.md)
  - [Cómo contribuir](CONTRIBUTING.md)
