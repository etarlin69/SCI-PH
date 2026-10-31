# Responsabilidades por rol

Este documento explica qué puede empezar a hacer cada rol **ahora**, sobre la arquitectura base de la Semana 1, y qué partes del repositorio toca principalmente, para trabajar en paralelo sin invadir las responsabilidades de otro.

> No se asignan personas en este documento; solo roles.

## Resumen

| Rol | Nombre | Toca principalmente |
|---|---|---|
| 1 | Technical Lead / Backend | `docs/`, `docs/adr/`, `docs/api/`, `src/Presentation/*` (composición), revisión de todo |
| 2 | Backend / Database | `src/Core/SCI-PH.Domain`, `src/Infrastructure/SCI-PH.Persistence` (Clinical), `Migrations/Clinical` |
| 3 | Backend / Security | `src/Infrastructure/SCI-PH.Persistence/Identity`, `IdentityDbContext`, `Migrations/Identity`, autenticación en los BFF |
| 4 | Frontend Web / Hospital | Proyecto React (fuera de `src/`), consume `Web.BFF` |
| 5 | Frontend Web / Administration | Proyecto React (fuera de `src/`), consume `Web.BFF` (`/api/web/admin`) |
| 6 | Mobile / Paramedics | Proyecto .NET MAUI (fuera de `src/`), consume `Mobile.BFF` (`/api/mobile/emergencies`) |
| 7 | Mobile / Patient | Proyecto .NET MAUI (fuera de `src/`), consume `Mobile.BFF` (`/api/mobile/auth`, `/me`) |
| 8 | AI / Biometrics / OCR / QR | `src/Core/SCI-PH.Application/Abstractions`, `src/Infrastructure/SCI-PH.Infrastructure` |
| 9 | Hardware / QA / DevOps | Firmware ESP32, `tests/`, `docker-compose.yml`, `.github/workflows/` |

> La ubicación de los proyectos React y .NET MAUI (este repositorio en una carpeta nueva, o repositorios separados) la decide el Rol 1 antes de crearlos.
>
> **Mobile (decidido):** una sola app MAUI en `mobile/SCI-PH.Mobile/`, con su propia solución `mobile/SCI-PH.Mobile.sln`; el Rol 6 trabaja en `Features/Paramedic` y el Rol 7 en `Features/Patient` (ver [mobile/README.md](../../mobile/README.md)).

## Reglas comunes

- Todo entra por Pull Request hacia `develop` (ver [CONTRIBUTING.md](../../CONTRIBUTING.md)). Nadie trabaja en `main`.
- Los contratos de `docs/api/` son el acuerdo entre clientes y backend. Para cambiarlos se abre un PR que actualice el documento; lo revisan el Rol 1 y los roles afectados.
- Las reglas de [dependency-rules.md](../architecture/dependency-rules.md) no se rompen sin un ADR.
- `ApplicationUser` (cuenta) y `Patient` (persona clínica) son conceptos distintos (ver [ADR-003](../adr/ADR-003-postgresql-two-contexts.md)).
- Nadie sube credenciales. La cadena de conexión va en user-secrets.

---

## Rol 1 — Technical Lead / Backend

**Responsabilidades:** arquitectura, contratos, integración, revisión de PR y decisiones técnicas.

**Toca principalmente**
- `docs/architecture/`, `docs/adr/`, `docs/api/`
- `src/Presentation/*/Program.cs` (composición y middleware común)
- `.github/pull_request_template.md`, `CONTRIBUTING.md`

**Puede empezar ahora**
- Configurar la protección de ramas `main` y `develop` en GitHub.
- Decidir dónde viven los proyectos React y MAUI.
- Redactar los ADRs pendientes: autenticación de usuarios (JWT), autenticación de dispositivos y estilo de casos de uso (servicios simples o librería de mediación).
- Revisar y aprobar los PRs de los demás roles.
- Agregar `CODEOWNERS` cuando estén definidos los usuarios de GitHub.

**No hace**
- Implementar funcionalidades que pertenecen a otro rol; desbloquea y revisa.

## Rol 2 — Backend / Database

**Responsabilidades:** EF Core, PostgreSQL, entidades, relaciones, configuraciones, migraciones clínicas y consultas.

**Toca principalmente**
- `src/Core/SCI-PH.Domain` (entidades clínicas: `Patient`, episodios, traslados…)
- `src/Infrastructure/SCI-PH.Persistence/Contexts/ClinicalDbContext.cs`
- `src/Infrastructure/SCI-PH.Persistence/Configurations/` (nueva, una `IEntityTypeConfiguration` por entidad)
- `src/Infrastructure/SCI-PH.Persistence/Migrations/Clinical/`
- `tests/SCI-PH.Domain.Tests`, `tests/SCI-PH.IntegrationTests`

**Puede empezar ahora**
- Modelar las entidades clínicas en Domain, sin referencias a EF Core.
- Mapearlas en `ClinicalDbContext` (schema `clinical`).
- Crear la primera migración de `ClinicalDbContext` cuando el modelo esté revisado.
- Implementar `IUnitOfWork` en Persistence.

**No hace**
- Relaciones EF entre `IdentityDbContext` y `ClinicalDbContext` (se relacionan por `Guid`).
- Cambios en el schema `identity` (Rol 3).

## Rol 3 — Backend / Security

**Responsabilidades:** ASP.NET Identity, JWT (posteriormente), roles, permisos, autorización y auditoría.

**Toca principalmente**
- `src/Infrastructure/SCI-PH.Persistence/Identity/`
- `src/Infrastructure/SCI-PH.Persistence/Contexts/IdentityDbContext.cs`
- `src/Infrastructure/SCI-PH.Persistence/Migrations/Identity/`
- Configuración de autenticación y autorización en `src/Presentation/*/Program.cs` (coordinado con Rol 1)

**Puede empezar ahora**
- Definir el catálogo de roles y permisos (quién puede hacer qué en Web, Mobile y Device).
- Diseñar el modelo de auditoría (qué se registra, dónde y por cuánto tiempo).
- Revisar y aprobar [ADR-006](../adr/ADR-006-identity-security.md) (cuentas, sesiones, MFA, invitaciones) y [ADR-007](../adr/ADR-007-device-authentication.md) (dispositivos), propuestos junto con su implementación en `feature/security-foundation`.
- Extender `ApplicationUser` si hace falta, con migración en `Migrations/Identity`.

**No hace**
- Datos clínicos en el schema `identity`.
- La integración con el proveedor de verificación facial (Rol 8); solo define cómo su resultado autoriza el acceso.

## Rol 4 — Frontend Web / Hospital

**Responsabilidades:** recepción, médicos, enfermería, transferencias, evolución y resultados.

**Toca principalmente**
- El proyecto React + TypeScript (sección hospital)
- [docs/api/web-bff-contracts.md](../api/web-bff-contracts.md) (solo mediante PR revisado)

**Puede empezar ahora**
- Crear la estructura del proyecto React y el layout del área hospitalaria.
- Construir las pantallas de transferencias con los contratos `GET /api/web/transfers` y `POST /api/web/transfers/{id}/receive` y datos simulados (mocks).
- Verificar la conexión con `GET /health` del Web BFF.

**No hace**
- Llamar al Mobile BFF, al Device API ni a la base de datos.
- Asumir campos que no están en el contrato; los pide mediante PR.

## Rol 5 — Frontend Web / Administration

**Responsabilidades:** usuarios, roles, catálogos, dispositivos, dashboards, auditoría y configuración.

**Toca principalmente**
- El proyecto React + TypeScript (sección administración)
- [docs/api/web-bff-contracts.md](../api/web-bff-contracts.md), sección *Web Administration* (solo mediante PR revisado)

**Puede empezar ahora**
- Pantallas de usuarios, roles, auditoría y dispositivos con los contratos `/api/web/admin/*` y mocks.
- Proponer los contratos de catálogos, dashboards y configuración que aún no están documentados.

**No hace**
- Definir por su cuenta reglas de permisos (Rol 3) ni el modelo de dispositivos (Rol 9 / Rol 1).

## Rol 6 — Mobile / Paramedics

**Responsabilidades:** emergencias, identificación, episodios, signos vitales, procedimientos y transferencias.

**Toca principalmente**
- El proyecto .NET MAUI (flujo de paramédicos)
- [docs/api/mobile-bff-contracts.md](../api/mobile-bff-contracts.md), sección *Paramedic Workflow* (solo mediante PR revisado)

**Puede empezar ahora**
- Pantallas del flujo de emergencia: crear emergencia, identificar paciente, registrar signos vitales, procedimientos y medicamentos, y transferir, con los contratos `/api/mobile/emergencies/*` y mocks.
- Diseñar el comportamiento sin conexión (captura local y envío posterior) para proponerlo al Rol 1.

**No hace**
- Decidir el algoritmo de identificación (Rol 8 / Rol 9) ni las reglas clínicas (Rol 2).

## Rol 7 — Mobile / Patient

**Responsabilidades:** QR, facial, perfil, historial, resultados, documentos e IA.

**Toca principalmente**
- El proyecto .NET MAUI (app del paciente)
- [docs/api/mobile-bff-contracts.md](../api/mobile-bff-contracts.md), sección del paciente (solo mediante PR revisado)

**Puede empezar ahora**
- Pantallas de escaneo QR, captura facial y perfil con los contratos `POST /api/mobile/auth/qr/challenge`, `POST /api/mobile/auth/face/verify` y `GET /api/mobile/me` y mocks.
- Proponer los contratos de historial, resultados, documentos y asistente de IA.

**No hace**
- Guardar imágenes faciales en el dispositivo más de lo necesario.
- Presentar la IA como diagnóstico: es un asistente **educativo** (hoy un chat simulado sin backend; no usa el resumen
  asistido del hospital, ver [ADR-013](../adr/ADR-013-clinical-ai-assistant.md)).

## Rol 8 — AI / Biometrics / OCR / QR

**Responsabilidades:** IA clínica asistida (resumen de la atención, [ADR-013](../adr/ADR-013-clinical-ai-assistant.md)), IA
educativa, Face Verification 1:1, OCR, QR y pruebas de proveedores.

**Toca principalmente**
- `src/Core/SCI-PH.Application/Abstractions/` (`AI`, `Biometrics`, `OCR`, `QR`, `Storage`, `Documents`)
- `src/Infrastructure/SCI-PH.Infrastructure/` y su `DependencyInjection.AddInfrastructure`

**Puede empezar ahora**
- Evaluar proveedores de IA, verificación facial 1:1 y OCR, y documentar la comparación.
- Proponer los métodos de `IOcrService` e `IQrService` (hoy son interfaces vacías) mediante PR revisado por el Rol 1.
  `IClinicalAiService` e `IFaceVerificationService` ya tienen contrato (ADR-013, ADR-012): una operación de IA nueva es
  otro método cerrado con su prompt versionado, nunca un prompt libre.
- Implementaciones de prueba (fakes) en Infrastructure para que los BFF puedan integrarse sin proveedor real.

**No hace**
- Agregar el SDK de Azure u otros proveedores sin ADR y sin revisión.
- Que la IA diagnostique o prescriba, o que su salida se guarde como dato del expediente.
- Enviar datos reales de pacientes a un proveedor externo sin el acuerdo de datos y la revisión de ADR-013 (decisión 12) y
  [ADR-014](../adr/ADR-014-clinical-ai-real-provider.md) (decisión 4); habilitar `ClinicalAi:AllowExternalClinicalData` es
  esa decisión, no un ajuste técnico.
- Referenciar SDKs externos desde Domain o Application.

## Rol 9 — Hardware / QA / DevOps

**Responsabilidades:** ESP32, R307, simulador de huella, Docker, CI/CD, QA y pruebas de integración.

**Toca principalmente**
- Firmware ESP32 (fuera de `src/`)
- [docs/api/device-api-contracts.md](../api/device-api-contracts.md) (solo mediante PR revisado)
- `tests/`, `docker-compose.yml`, `.github/workflows/`

**Puede empezar ahora**
- Firmware del ESP32 con el R307, enviando `POST /api/device/heartbeat` y `POST /api/device/fingerprint/identify` según el contrato.
- Un simulador de huella (cliente HTTP) para probar el Device API sin hardware.
- Pruebas de integración con PostgreSQL real en `tests/SCI-PH.IntegrationTests`.
- Mantener CI (`build.yml`) y agregar reportes de pruebas.

**No hace**
- Modificar código productivo para que una prueba pase: reporta el defecto al rol dueño.
- Agregar deploy o secretos de Azure hasta que se decida la estrategia de despliegue.
