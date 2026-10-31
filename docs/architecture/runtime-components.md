# Componentes en ejecución

## Vista general

```
┌────────────────────┐      HTTPS      ┌──────────────────┐
│ Web (React + TS)   │ ──────────────► │  SCI-PH.Web.BFF  │ ──┐
└────────────────────┘                 └──────────────────┘   │
                                                              │
┌────────────────────┐      HTTPS      ┌──────────────────┐   │    ┌─────────────────────────┐
│ Mobile (.NET MAUI) │ ──────────────► │ SCI-PH.Mobile.BFF│ ──┼──► │ PostgreSQL  (sciph_dev) │
└────────────────────┘                 └──────────────────┘   │    │  ├─ schema identity     │
                                                              │    │  └─ schema clinical     │
┌────────────────────┐      HTTP(S)    ┌──────────────────┐   │    └─────────────────────────┘
│ ESP32 + R307       │ ──────────────► │ SCI-PH.Device.API│ ──┤
└────────────────────┘                 └──────────────────┘   │    ┌─────────────────────────┐
                                                              ├──► │ Azure Blob Storage      │
                                                              │    └─────────────────────────┘
                                                              │    ┌─────────────────────────┐
                                                              └──► │ Servicios externos      │
                                                                   │ (IA, OCR, biometría)    │
                                                                   └─────────────────────────┘
```

Cada cliente habla **solo** con su propio BFF o API:

| Cliente | Punto de entrada |
|---|---|
| React (web) | Web BFF |
| .NET MAUI (móvil) | Mobile BFF |
| ESP32 + R307 | Device API |

Los tres puntos de entrada son procesos independientes que comparten Domain, Application, Persistence e Infrastructure.

## Componentes

### Web BFF
Backend de la aplicación web. Atiende dos áreas: **Hospital** (recepción, médicos, enfermería, traslados, evolución y resultados) y **Administración** (`/api/web/admin`: usuarios, roles, auditoría, dispositivos y configuración).

### Mobile BFF
Backend de la app móvil. Atiende dos flujos: **Patient** (acceso mediante QR y verificación facial 1:1, perfil, historial, documentos e IA) y **Paramedic** (`/api/mobile/emergencies`: emergencias, identificación, signos vitales, procedimientos, medicamentos y traslados).

### Device API
Recibe peticiones de los dispositivos ESP32 con sensor de huella R307: identificación por huella para emergencias (el sensor compara; el dispositivo recoge su sesión y envía el resultado, sin recibir datos clínicos, [ADR-009](../adr/ADR-009-fingerprint-identification.md)) y heartbeat del dispositivo. La autenticación del dispositivo es distinta de la de usuarios ([ADR-007](../adr/ADR-007-device-authentication.md)).

### PostgreSQL
Una sola base física (`sciph_dev` en desarrollo) con dos schemas:

| Schema | DbContext | Contenido |
|---|---|---|
| `identity` | `IdentityDbContext` | Cuentas, roles, claims, logins y tokens de ASP.NET Core Identity |
| `clinical` | `ClinicalDbContext` | Datos del dominio clínico, incluida la metadata de documentos |

Cada contexto tiene su tabla `__EFMigrationsHistory` dentro de su schema. Ver [ADR-003](../adr/ADR-003-postgresql-two-contexts.md).

### Azure Blob Storage
Guarda los archivos físicos (documentos, imágenes). PostgreSQL guarda solo la metadata. Ver [ADR-004](../adr/ADR-004-document-storage.md).

### Servicios externos

| Servicio | Uso | Límites |
|---|---|---|
| IA | Resumen **asistido** de una atención para el personal del hospital ([ADR-013](../adr/ADR-013-clinical-ai-assistant.md)); asistente **educativo** del paciente (simulado) | **No diagnostica. No prescribe.** Solo lectura, sin guardar lo generado y siempre con revisión profesional. Fake en proceso por defecto (CI y desarrollo); Azure OpenAI ([ADR-014](../adr/ADR-014-clinical-ai-real-provider.md)) solo con el permiso explícito `ClinicalAi:AllowExternalClinicalData` y, por ahora, solo con datos sintéticos. |
| Verificación facial | Verificación **1:1**: confirma que la persona es quien dice ser, comparándola con una referencia conocida | No se usa para identificar entre muchos (1:N) |
| Huella (R307) | **Identificación** del paciente en **emergencias**, cuando no puede identificarse por sí mismo | Solo para el flujo de emergencia |
| OCR | Extracción de texto de documentos | — |
| QR | Generación y validación de códigos para el flujo de acceso móvil | — |

## Endpoints técnicos comunes

| Ruta | Disponible | Propósito |
|---|---|---|
| `/health` | Siempre | Health check |
| `/openapi/v1.json` | Solo Development | Documento OpenAPI |

Los errores se devuelven como `application/problem+json` (ProblemDetails, RFC 7807).

## Entorno local

| Componente | Cómo se ejecuta | Dirección |
|---|---|---|
| PostgreSQL 17 | `docker compose up -d postgres` | `127.0.0.1:5432` |
| Web BFF | `dotnet run --project src/Presentation/SCI-PH.Web.BFF` | http://localhost:5074 |
| Mobile BFF | `dotnet run --project src/Presentation/SCI-PH.Mobile.BFF` | http://localhost:5191 |
| Device API | `dotnet run --project src/Presentation/SCI-PH.Device.API` | http://localhost:5139 |
