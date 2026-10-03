# Cómo contribuir a SCI-PH

## Ramas

| Rama | Propósito |
|---|---|
| `main` | Estable. Solo recibe merges desde `develop`. |
| `develop` | Integración. Aquí se juntan las funcionalidades terminadas. |
| `feature/*` | Funcionalidades nuevas. Ej.: `feature/mobile-qr-challenge` |
| `bugfix/*` | Correcciones. Ej.: `bugfix/health-check-timeout` |

### Flujo

```
feature/*  ─┐
            ├──►  develop  ──►  main
bugfix/*   ─┘
```

1. Crea tu rama desde `develop` actualizado:

   ```bash
   git switch develop
   git pull
   git switch -c feature/<descripcion-corta>
   ```

2. Haz commits pequeños y con mensajes claros.
3. Abre un Pull Request hacia `develop`.
4. Cuando `develop` esté estable, se abre un PR de `develop` hacia `main`.

**Nadie desarrolla directamente en `main`.** Tampoco se hace push directo a `develop`: todo entra por Pull Request.

## Pull Requests

Cada PR debe:

- Compilar: `dotnet build` sin errores.
- Pasar las pruebas: `dotnet test` en verde.
- Pasar el workflow de GitHub Actions (`.github/workflows/build.yml`), que ejecuta restore, build y test.
- Completar la plantilla de PR (`.github/pull_request_template.md`), incluida la sección de impacto en base de datos.
- Tener al menos una revisión aprobada antes del merge.
- Mantenerse enfocado: un PR = un cambio coherente.

## Reglas de arquitectura

Antes de agregar una referencia entre proyectos o un paquete NuGet, revisa [docs/architecture/dependency-rules.md](docs/architecture/dependency-rules.md). En resumen:

- `Domain` no depende de nada.
- `Application` solo depende de `Domain`.
- `Persistence` e `Infrastructure` dependen de `Application` y `Domain`.
- Los BFF / Device API son los únicos que componen todo.

Los cambios que alteren estas reglas, los contratos de API o el modelo de datos requieren un ADR nuevo en `docs/adr/` y revisión arquitectónica.

## Migraciones de base de datos

- Cada `DbContext` tiene su propia carpeta de migraciones:
  - `IdentityDbContext` → `src/Infrastructure/SCI-PH.Persistence/Migrations/Identity`
  - `ClinicalDbContext` → `src/Infrastructure/SCI-PH.Persistence/Migrations/Clinical`
- Revisa la migración generada antes de subirla.
- No edites migraciones que ya estén en `develop`; crea una nueva.
- Si dos PRs agregan migraciones al mismo contexto, el segundo debe regenerar la suya después de integrar el primero.
- Escribe el SQL de `HasCheckConstraint` y `HasFilter` en una sola línea (usa `Configurations/Sql.cs`); un texto multilínea cambia entre LF y CRLF y genera cambios pendientes falsos.
- Antes de subir, el modelo no debe tener cambios pendientes (el test `MigrationSnapshotMatchesTheModel` también lo comprueba).

Ejemplo:

```bash
dotnet ef migrations add <Nombre> --context ClinicalDbContext --project src/Infrastructure/SCI-PH.Persistence --startup-project src/Presentation/SCI-PH.Web.BFF --output-dir Migrations/Clinical
dotnet ef migrations has-pending-model-changes --context ClinicalDbContext --project src/Infrastructure/SCI-PH.Persistence --startup-project src/Presentation/SCI-PH.Web.BFF
```

## Secretos y configuración

- Nunca subas contraseñas, cadenas de conexión reales, claves de Azure ni tokens.
- Usa `dotnet user-secrets` en desarrollo y `.env` (ignorado por git) para Docker.
- `.env.example` y `appsettings*.json` solo deben contener valores de ejemplo o no sensibles.

## Estilo de código

- El repositorio incluye `.editorconfig`; usa el formateo del IDE.
- Namespaces con llaves, `using` fuera del namespace, llaves en línea propia.
- Interfaces con prefijo `I`, campos privados con prefijo `_`.
