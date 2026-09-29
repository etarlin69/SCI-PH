# ADR-003: PostgreSQL con dos DbContexts y dos schemas

- **Estado:** Aceptado
- **Fecha:** 2026-09-27

## Contexto

SCI-PH necesita guardar dos tipos de datos con ciclos de vida distintos:

- **Identidad:** cuentas que pueden autenticarse, roles, claims y tokens (ASP.NET Core Identity).
- **Datos clínicos:** pacientes y su información médica.

Una cuenta (`ApplicationUser`) y un paciente (`Patient`) **no son lo mismo**:

- Un paciente puede no tener cuenta (por ejemplo, alguien identificado por huella en una emergencia).
- Una cuenta puede no ser paciente (por ejemplo, personal de salud).

Mezclar ambos modelos en un solo contexto acoplaría el dominio clínico a Identity.

## Decisión

- Motor: **PostgreSQL** (Npgsql + EF Core 9).
- **Una base física** (`sciph_dev` en desarrollo).
- **Dos DbContexts**, cada uno en su propio **schema**:

  | DbContext | Schema | Contenido |
  |---|---|---|
  | `IdentityDbContext` | `identity` | `ApplicationUser`, roles, claims, logins, tokens |
  | `ClinicalDbContext` | `clinical` | Entidades del dominio clínico |

- Cada contexto guarda su historial de migraciones en su propio schema (`identity.__EFMigrationsHistory`, `clinical.__EFMigrationsHistory`).
- Las migraciones se guardan en carpetas separadas: `Migrations/Identity` y `Migrations/Clinical`.
- Ambos contextos usan la misma cadena de conexión: `ConnectionStrings:SciPhDatabase`.
- Si hace falta relacionar una cuenta con un paciente, se hace **por identificador (`Guid`)**, sin navegación de EF entre contextos ni claves foráneas entre schemas.
- `ApplicationUser` vive en Persistence, no en Domain.

## Consecuencias

**Positivas**
- El dominio clínico no depende de Identity.
- Las migraciones de cada área evolucionan de forma independiente.
- Una sola base simplifica la operación y los respaldos en esta etapa.
- Si en el futuro fuera necesario, cada schema puede moverse a su propia base con cambios acotados.

**Negativas**
- No hay transacciones de EF que abarquen ambos contextos de forma nativa.
- La integridad entre cuenta y paciente debe garantizarla la aplicación, no la base.

**Estado actual**
- `InitialIdentity` creada y aplicada en local.
- `ClinicalDbContext` todavía no tiene migraciones.
