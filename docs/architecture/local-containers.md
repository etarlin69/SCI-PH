# Stack local en contenedores

`docker-compose.yml` levanta SCI-PH completo en local, sin instalar .NET ni Node:

| Servicio | Imagen | Dirección en el host | Notas |
|---|---|---|---|
| `postgres` | `postgres:17-alpine` | `127.0.0.1:${POSTGRES_PORT:-5432}` | Contenedor `sci-ph-postgres`, volumen `sci-ph-postgres-data`. Una sola base para `IdentityDbContext` y `ClinicalDbContext`. |
| `azurite` | `azurite:3.37.0` (solo Blob) | `127.0.0.1:${AZURITE_BLOB_PORT:-10000}` | Volumen `sci-ph-azurite-data` |
| `migrations` | Web BFF, etapa `migrations` | — | Tarea de una vez: `dotnet ef database update` de Identity y Clinical; espera a que PostgreSQL esté sano |
| `web-bff` | Web BFF | `127.0.0.1:${WEB_BFF_PORT:-5074}` | Arranca cuando las migraciones terminan bien; crea el administrador inicial |
| `mobile-bff` | Mobile BFF | `127.0.0.1:${MOBILE_BFF_PORT:-5191}` | |
| `device-api` | Device API | `127.0.0.1:${DEVICE_API_PORT:-5139}` | En Development acepta credenciales de dispositivo por HTTP |
| `web` | web-admin + nginx | `127.0.0.1:${WEB_PORT:-5173}` | Sirve el front y reenvía `/api/` al Web BFF (mismo origen) |

Todos los puertos se publican solo en `127.0.0.1`. Los servicios corren con `ASPNETCORE_ENVIRONMENT=Development`.

## Puesta en marcha

1. Copia la plantilla: `cp .env.example .env` (`.env` está en `.gitignore`; nunca se sube).
2. Reemplaza los valores de ejemplo:
   - `POSTGRES_PASSWORD`: una contraseña local.
   - `JWT_SIGNING_KEY`: una clave propia de al menos 32 bytes, por ejemplo `openssl rand -base64 48`. El valor
     `REPLACE_ME` es demasiado corto a propósito: con él los BFF no arrancan.
   - `IDENTITY_BOOTSTRAP_ADMIN_EMAIL` e `IDENTITY_BOOTSTRAP_ADMIN_PASSWORD`: el administrador inicial. La contraseña debe
     cumplir la política (mayúsculas, minúsculas, dígito y símbolo); con `REPLACE_ME` el Web BFF no arranca.
     El seeder es idempotente: crea la cuenta solo si el email no existe y nunca escribe la contraseña en los logs.
     Con `IDENTITY_BOOTSTRAP_ENABLED=false`, o sin email/contraseña, solo se sincronizan los roles.
   - `IDENTITY_DEV_ACCOUNTS_PASSWORD`: la contraseña de las cuentas de prueba (misma política). Ver
     [Cuentas de prueba](#cuentas-de-prueba).
3. Levanta el stack:

   ```bash
   docker compose up -d --build
   ```

## Comprobar

```bash
docker compose ps                       # postgres healthy, migrations "exited (0)", el resto running
docker compose logs migrations          # resultado de las migraciones
curl http://localhost:5074/health       # Web BFF
curl http://localhost:5191/health       # Mobile BFF
curl http://localhost:5139/health       # Device API
curl http://localhost:5173/health       # web-admin (nginx)
```

El front queda en http://localhost:5173; inicia sesión con el administrador inicial.

## Cuentas de prueba

Con `IDENTITY_DEV_ACCOUNTS_ENABLED=true` (valor de `.env.example`) el Web BFF crea al arrancar una cuenta por rol, todas
con la contraseña `IDENTITY_DEV_ACCOUNTS_PASSWORD` de tu `.env`:

| Cuenta | Rol | Para probar |
|---|---|---|
| El email de `IDENTITY_BOOTSTRAP_ADMIN_EMAIL` | Administrator | Panel web (usuarios, roles, invitaciones) |
| `doctor@sci-ph.test` | Doctor | Web hospitalaria: atenciones, diagnósticos y prescripciones |
| `nurse@sci-ph.test` | Nurse | Web hospitalaria: signos vitales y administración de medicamentos; en la app móvil, acceso denegado |
| `paramedic@sci-ph.test` | Paramedic | App móvil, acceso del personal clínico (`Emergencies.Operate`) |
| `receptionist@sci-ph.test` | Receptionist | Registro de pacientes e invitaciones de paciente |
| `patient@sci-ph.test` | Patient | Cuenta de paciente sin expediente vinculado |

También se crea la clínica de desarrollo (`DEV-SCIPH-01`) con el doctor, la enfermera, el paramédico y la recepcionista
como personal clínico vinculado a esas cuentas, y el catálogo de medicamentos de demostración.

- Solo en Development: si `IdentityDevelopmentAccounts:Enabled` es `true` en otro entorno, el Web BFF no arranca.
- Es idempotente: crea cada cuenta una vez. Cambiar la contraseña en `.env` no cambia la de una cuenta ya creada; para
  eso usa el cambio de contraseña de la propia cuenta.
- Un email que ya existe sin ese rol (por ejemplo una cuenta real) no se modifica.
- Las contraseñas nunca se escriben en los logs ni en el repositorio: cada persona del equipo pone la suya en su `.env`.
- Sin `docker compose` (con `dotnet run`), configura lo mismo en User Secrets del Web BFF:
  `IdentityDevelopmentAccounts:Enabled`, `IdentityDevelopmentAccounts:Password` y
  `IdentityDevelopmentAccounts:Accounts:N:Email` / `:Role`.

## Detener sin perder datos

```bash
docker compose down        # para y elimina los contenedores; los volúmenes se conservan
```

PostgreSQL y Azurite guardan sus datos en volúmenes con nombre (`sci-ph-postgres-data`, `sci-ph-azurite-data`), que
`docker compose down` no toca. **No uses `docker compose down -v`** en el uso normal: borra esos volúmenes y con ellos
la base de datos local.

## Solo la base de datos

Para desarrollar con `dotnet run` (ver el README), levanta únicamente PostgreSQL:

```bash
docker compose up -d postgres
```

El contenedor es el mismo `sci-ph-postgres` con el mismo volumen, así que la base local existente se reutiliza. No
ejecutes a la vez el stack completo y los servicios con `dotnet run`: usan los mismos puertos.

## Simulador de dispositivo

`tools/SCI-PH.Device.Simulator` simula un dispositivo registrado (`POST /api/web/admin/devices`) contra el Device API;
la clave se lee de variables de entorno y no se imprime. Solo envía datos técnicos, nunca datos clínicos. Por defecto
envía heartbeats:

```bash
DEVICE_ID=<id> DEVICE_KEY=<clave> dotnet run --project tools/SCI-PH.Device.Simulator
```

Opcionales: `DEVICE_API_BASE_URL` (por defecto `http://localhost:5139`), `DEVICE_FIRMWARE_VERSION` y
`DEVICE_HEARTBEAT_INTERVAL_SECONDS`.

Con `DEVICE_MODE=fingerprint` simula un lector R307 (scope `Device.Fingerprint.Identify`, ADR-009): consulta su sesión
pendiente, espera la "captura" y envía el resultado del sensor. El slot no se imprime.

```bash
DEVICE_MODE=fingerprint FINGERPRINT_OUTCOME=Match FINGERPRINT_SLOT=1 DEVICE_ID=<id> DEVICE_KEY=<clave> \
  dotnet run --project tools/SCI-PH.Device.Simulator
```

- `FINGERPRINT_OUTCOME`: `Match` (por defecto; exige `FINGERPRINT_SLOT`, entero no negativo), `NoMatch`, `LowQuality`
  o `SensorError`.
- `FINGERPRINT_POLL_INTERVAL_SECONDS` (por defecto 2), `FINGERPRINT_RESULT_DELAY_SECONDS` (captura simulada, por
  defecto 1), `FINGERPRINT_REPEAT_RESULT=true` (repite el mismo resultado, como tras una respuesta perdida) y
  `FINGERPRINT_ONCE=true` (sale tras el primer resultado: código 0 si se aceptó).
- Con `DEVICE_MODE=enroll` simula un lector que **registra** huellas (scope `Device.Fingerprint.Enroll`, ADR-010): consulta
  su registro pendiente, guarda la plantilla en el primer slot libre de su "sensor" (un archivo JSON de slots ocupados,
  `SIMULATOR_SLOTS_FILE`, por defecto en la carpeta temporal) y lo informa. Conserva el slot solo tras un 204; ante una
  respuesta incierta reintenta el mismo resultado y, ante un rechazo o el vencimiento, borra el slot (compensación).
  Opcionales: `ENROLL_OUTCOME` (`Enrolled` por defecto, `Failed`, `LowQuality`, `SensorError`), `ENROLL_SLOT` (slot fijo),
  `ENROLL_FIRST_SLOT` (por defecto 1), `ENROLL_LOSE_FIRST_RESPONSE=true` (simula una respuesta perdida),
  `ENROLL_CHANGED_REPLAY=true` (envía después otro resultado, que debe dar 409), y los mismos `FINGERPRINT_*` de retardo,
  repetición, intervalo y `FINGERPRINT_ONCE`.
- El mismo modo `enroll` **borra** los slots de huellas revocadas (ADR-011): consulta su borrado pendiente, quita el slot
  de su archivo (`Deleted`) o informa que no estaba (`NotFound`), con un `attemptId` nuevo por intento físico; ante una
  respuesta incierta reenvía el mismo intento. Opcionales: `DELETE_SENSOR_ERRORS` (cuántos intentos fallan con
  `SensorError` antes de que el sensor funcione, por defecto 0; no tocan el archivo), `DELETE_RETRY_SECONDS` (espera
  entre intentos, por defecto 3), `DELETE_LOSE_FIRST_RESPONSE=true` (simula un 204 perdido: reenvía el mismo intento) y
  `DELETE_CHANGED_REPLAY=true` (reenvía el mismo intento con otro resultado, que debe dar 409).

## Verificación facial

El Web BFF usa el proveedor facial de `FaceVerification:Provider` ([ADR-012](../adr/ADR-012-face-verification.md)). Sin
configurar, en Development (el stack local) es `Fake`: un proveedor determinista que **no reconoce caras** (decide por el
color medio de capturas sintéticas) y no necesita modelos ni claves. En Production, sin configurar, es `Disabled` (503) y
`Fake` impide arrancar. El proveedor local real (OpenCV: YuNet + SFace) llega en el PR 2.
- Para que un `Match` tenga candidato, el paciente debe estar registrado en ese lector (registro desde el Web con el modo
  `enroll`); en Development también se puede sembrar la referencia del paciente con
  `ClinicalDevelopmentSeed:FingerprintReferences` (`PatientId`, `DeviceCredentialId` y `Slot`; User Secrets, nunca el
  repositorio). Solo Development: no hay semilla biométrica de producción.

## CI

El job `containers` de `.github/workflows/build.yml` valida `docker compose config` y construye las imágenes con
valores de CI efímeros. No publica imágenes ni despliega nada.
