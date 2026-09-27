# Reglas de dependencia

Las dependencias apuntan siempre **hacia el centro** (Domain). Una capa interna nunca conoce a una externa.

```
                    ┌──────────────┐
                    │    Domain    │
                    └──────▲───────┘
                           │
                    ┌──────┴───────┐
                    │ Application  │
                    └──▲────────▲──┘
                       │        │
          ┌────────────┴─┐   ┌──┴──────────────┐
          │ Persistence  │   │ Infrastructure  │
          └──────▲───────┘   └───────▲─────────┘
                 │                   │
     ┌───────────┴───────────────────┴───────────┐
     │   Web.BFF   │   Mobile.BFF   │ Device.API  │
     └─────────────────────────────────────────────┘
```

## Referencias permitidas entre proyectos

| Proyecto | Puede referenciar |
|---|---|
| Domain | *(nada)* |
| Application | Domain |
| Persistence | Application, Domain |
| Infrastructure | Application, Domain |
| Web.BFF | Application, Persistence, Infrastructure |
| Mobile.BFF | Application, Persistence, Infrastructure |
| Device.API | Application, Persistence, Infrastructure |

## Prohibido

- **Domain** no puede depender de: EF Core, PostgreSQL/Npgsql, ASP.NET Core, Azure SDK, ASP.NET Core Identity, Persistence ni Infrastructure.
- **Application** no puede depender de Persistence ni de Infrastructure. Si necesita algo externo, define una abstracción en `Application/Abstractions` y la implementa una capa externa.
- **Persistence** e **Infrastructure** no se referencian entre sí.
- Un BFF no referencia a otro BFF ni a Device.API.
- Los controllers no acceden a `DbContext` directamente: pasan por Application.

## Paquetes por capa

| Capa | Paquetes permitidos (orientativo) |
|---|---|
| Domain | Ninguno |
| Application | Abstracciones de `Microsoft.Extensions.*`. Librerías de validación o mediación solo mediante ADR. |
| Persistence | EF Core, Npgsql, Identity.EntityFrameworkCore |
| Infrastructure | Azure SDK, clientes de OCR/IA/biometría, abstracciones de `Microsoft.Extensions.*` |
| BFF / Device API | ASP.NET Core, OpenAPI, autenticación |

## Identity vs. Patient

Son conceptos distintos y viven en capas distintas:

| Concepto | Qué representa | Dónde vive | Schema |
|---|---|---|---|
| `ApplicationUser` | Una **cuenta** que puede autenticarse | Persistence (`Identity/Models`) | `identity` |
| `Patient` | Una **persona** del dominio clínico | Domain | `clinical` |

- Un `Patient` puede existir sin cuenta (por ejemplo, un paciente atendido en emergencia).
- Una cuenta puede no ser un paciente (por ejemplo, personal de salud).
- Si se relacionan, se hace por identificador (`Guid`), sin navegación de EF entre `IdentityDbContext` y `ClinicalDbContext`.

## Registro de dependencias

Cada capa expone un único método de extensión, y los composition roots los invocan en este orden:

```csharp
builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
```

## Cómo cambiar una regla

Cualquier excepción a estas reglas requiere un ADR nuevo en `docs/adr/` aprobado mediante revisión arquitectónica.
