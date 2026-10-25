# SCI-PH Mobile

Una sola aplicación .NET MAUI para el personal clínico prehospitalario y para los pacientes. Vive en su propia
solución, separada del backend:

| Solución | Contenido |
|---|---|
| `SCI-PH.sln` (raíz) | Backend: Domain, Application, Persistence, Infrastructure, BFFs, Device API y tests |
| `mobile/SCI-PH.Mobile.sln` | App MAUI `mobile/SCI-PH.Mobile/SCI-PH.Mobile.csproj` y sus tests `mobile/tests/SCI-PH.Mobile.Tests` |

La app no referencia proyectos del backend: se comunica con `SCI-PH.Mobile.BFF` (`/api/mobile`) por HTTP, según
[docs/api/mobile-bff-contracts.md](../docs/api/mobile-bff-contracts.md), con DTOs propios.
Hoy son reales el acceso del personal clínico (`api/mobile/auth`) y la apertura, lectura e identificación por documento
de la emergencia (`api/mobile/emergencies`); el resto sigue simulado.

## Estructura

```text
mobile/
  SCI-PH.Mobile.sln
  SCI-PH.Mobile/
    App.xaml / App.xaml.cs        Recursos globales y ventana principal
    AppShell.xaml / .cs           Shell único: arranque, entrada compartida y área del paciente (menú con sesión)
    MauiProgram.cs                AddCoreServices() + AddAuthFeature() + AddParamedicFeature() + AddPatientFeature()
    Common/                       Compartido por todas las features
      Api/                        Cliente del Mobile BFF: MobileApiOptions, MobileApiClient, errores y
                                  AuthenticatedHttpHandler (Bearer + refresh ante 401)
      Authentication/             Sesión del personal clínico: MobileAuthService, ClinicalSessionService (memoria),
                                  ISecureTokenStore (SecureStorage), contratos de api/mobile/auth
      Startup/                    StartupPage: restaura la sesión clínica guardada antes de mostrar nada
      Entry/                      EntryPage + EntryViewModel: elegir personal clínico o paciente
      Navigation/                 IAppNavigator (implementación sobre Shell), AppRoutes
      Dialogs/                    IAppDialogService
      Location/                   IDeviceLocationService (ubicación puntual de mejor esfuerzo)
      ViewModels/                 BaseViewModel (IsBusy/IsNotBusy, ErrorMessage, RunAsync)
    Extensions/                   AddCoreServices(): registro de lo común y de la entrada
    Features/
      Auth/                       Login y MFA del personal clínico; cierre de sesión (ClinicalAccountViewModel)
      Paramedic/                  Rol 6: emergencias prehospitalarias
      Patient/                    Rol 7: acceso QR + rostro y área del paciente
    Resources/                    Fuentes, iconos, splash, avatar del paciente y estilos
                                  (SciPhTheme compartido; PatientTheme con las claves del paciente)
    Platforms/                    Android, iOS, Mac Catalyst, Windows, Tizen
  tests/
    SCI-PH.Mobile.Tests/          xUnit (net9.0): ViewModels y servicios con fakes, sin UI
```

Reglas de cada feature:

- Un `XxxServiceCollectionExtensions.AddXxxFeature()` registra sus servicios, ViewModels y páginas, y un
  `RegisterXxxRoutes()` registra sus rutas apiladas. Las rutas son constantes de `XxxRoutes`; ningún XAML ni
  ViewModel escribe la cadena de una ruta.
- Las páginas solo asignan el `BindingContext` (y, si hace falta, cargan datos en `OnAppearing`); la lógica está en
  ViewModels que heredan de `BaseViewModel` y usan `IAppNavigator`/`IAppDialogService`, nunca `Shell.Current`.
- Los servicios externos están detrás de interfaces. Salvo la autenticación clínica y la apertura y lectura de la
  emergencia, hoy todos son mocks en memoria; al conectar el Mobile BFF solo cambian los registros y se usa
  `IMobileApiClient` (nunca `HttpClient` en un ViewModel).
- Los enums de los contratos se serializan por nombre (`JsonStringEnumConverter<T>`), así que sus valores numéricos
  no tienen que coincidir con los del Domain.

## Entrada a la aplicación

```text
Arranque ─┬─ refresh token guardado → refresh + /me + Emergencies.Operate → área paramédica
          │                            └─ rechazado → se borra → entrada
          └─ sin token o sin conexión → entrada

Entrada compartida
  ├─ Personal clínico → login real (Mobile BFF) → [MFA] → /me → Emergencies.Operate → área paramédica
  └─ Paciente (Patient) → QR → verificación facial 1:1 → sesión | hoy: los dos pasos son simulados
```

Los dos accesos se apilan sobre la entrada, así que "Atrás" permite volver a elegir.

- Personal clínico: inicio de sesión real (ver "Acceso del personal clínico"); después, Nueva emergencia y el resto
  del flujo paramédico, sin menú lateral, con "Cerrar sesión" en la barra de Nueva emergencia.
- Paciente: el QR solo inicia el acceso (no autentica); la sesión se crea tras la verificación facial y abre el área
  del paciente. El menú lateral, con el nombre de la sesión, solo se habilita con sesión de paciente; "Cerrar
  sesión" la borra y vuelve a la entrada.

## Acceso del personal clínico

Contrato: `api/mobile/auth` del Mobile BFF (`login`, `mfa/verify`, `refresh`, `logout`, `me`). Sin cookies ni CSRF:
el refresh token viaja en el cuerpo.

| Dato | Dónde vive | Cuánto |
|---|---|---|
| Access token y su caducidad | Memoria (`ClinicalSessionService`) | Mientras la app está abierta |
| Refresh token | `SecureStorage` (`MauiSecureTokenStore`) | Hasta logout, rechazo del servidor o caducidad |
| Usuario (`/me`: id, usuario, email, roles, permisos) | Memoria | Mientras la sesión es válida |
| Challenge MFA | Memoria (`PendingMfaChallenge`) | Solo entre el login y la verificación |
| Contraseña, código TOTP, código de recuperación | En ningún sitio | Se borran del estado al enviarlos |

- **Login**: correo o usuario y contraseña. Si la cuenta tiene MFA no se crea sesión: se pide el código de 6 dígitos
  del autenticador o, alternativamente, un código de recuperación (`mfa/verify` con `code` o `recoveryCode`).
- **Autorización**: toda sesión nueva (login, MFA, restauración) llama a `/me` y exige el permiso
  `Emergencies.Operate`; nunca se decide por rol. Sin él, la sesión se revoca en el servidor, se borra y se muestra
  "Tu cuenta no tiene acceso al módulo prehospitalario.".
- **Peticiones protegidas** (`IMobileApiClient`): `AuthenticatedHttpHandler` añade `Authorization: Bearer`. Ante un
  401 hace un único refresh (single-flight: con varias peticiones a la vez solo una llama a `/refresh`), guarda el
  refresh token rotado y repite la petición una vez. Si la repetición vuelve a dar 401, o el refresh es rechazado
  (caducado, revocado o reutilizado), se borra la sesión y la app vuelve a la entrada. Login, MFA, refresh y logout
  usan un cliente sin ese handler, así que un refresh nunca dispara otro.
- **Arranque**: con refresh token guardado se renueva y se valida con `/me` antes de mostrar el área paramédica. Si el
  servidor lo rechaza, se borra; si `/refresh` no tiene conexión, se conserva para el siguiente arranque y se muestra la
  entrada. Si `/refresh` funciona pero `/me` falla, el token ya rotado se revoca (si hay conexión) y se borra: no queda
  una sesión a medias y hay que volver a iniciar sesión.
- **Logout**: revoca el refresh token en el servidor si hay conexión y siempre borra la sesión local. No toca la
  sesión del paciente.
- **Errores**: nunca se muestra ProblemDetails, el estado HTTP ni el texto de una excepción. Un fallo de red muestra
  "No fue posible conectar con SCI-PH. Intenta nuevamente." (nunca "credenciales incorrectas"); un 429 respeta
  `Retry-After`.

### Dirección del Mobile BFF

`MobileApiOptions` la resuelve al arrancar, sin IPs personales en el repositorio:

| Compilación | Plataforma | Dirección |
|---|---|---|
| Debug | Windows (y el resto) | `http://localhost:5191` |
| Debug | Emulador Android | `http://10.0.2.2:5191` (10.0.2.2 es el PC anfitrión) |
| Cualquiera | Cualquiera | `-p:MobileBffBaseUrl=...` al compilar tiene prioridad; en Release solo se acepta `https://` |
| Release | Cualquiera | Sin dirección salvo `MobileBffBaseUrl`: la app avisa que no tiene servidor configurado |

Android bloquea HTTP sin cifrar. Solo en **Debug** se incluye
`Platforms/Android/DebugResources/xml/network_security_config.xml`, que lo permite únicamente hacia `10.0.2.2` y
`localhost`; Release no declara network security config y además fija `usesCleartextTraffic="false"`, así que el
tráfico HTTP queda bloqueado también en Android 6-8 (donde el bloqueo por defecto de Android 9 no aplica). La
validación TLS nunca se desactiva.

Dispositivo Android físico, sin poner una IP en el repositorio:

- Con cable (Debug): `adb reverse tcp:5191 tcp:5191` y compilar con `-p:MobileBffBaseUrl=http://localhost:5191`.
- O un Mobile BFF accesible por HTTPS: `-p:MobileBffBaseUrl=https://<host>`.

El Mobile BFF local corre con `dotnet run --project src/Presentation/SCI-PH.Mobile.BFF` (puerto 5191) o con
`docker compose` (ver [docs/architecture/local-containers.md](../docs/architecture/local-containers.md)).

## Paramedic (estado actual)

| Pantalla | Qué hace |
|---|---|
| Nueva emergencia | Motivo de consulta. Abre la emergencia **real** en el Mobile BFF; los reintentos reutilizan `clientRequestId`, `startedAt` y el motivo. |
| Identificar paciente | Huella, documento (cédula o pasaporte) o continuar como paciente no identificado (NN), las tres desde el inicio. |
| Signos vitales | Tomas con límites de plausibilidad del Domain (`VitalSignLimits`); no son umbrales clínicos. |
| Procedimientos | Registro desde el catálogo. |
| Medicamentos | Registro de lo ya administrado (SCI-PH no prescribe). |

Se entra con la sesión real del personal clínico. `HttpParamedicApiService` es híbrido:

| Operación | Origen |
|---|---|
| Crear emergencia (`POST /api/mobile/emergencies`) | **REAL** (cliente autenticado, `IMobileApiClient`) |
| Leer emergencia (`GET /api/mobile/emergencies/{id}`) | **REAL** |
| Identificar por documento (`POST /api/mobile/emergencies/{id}/identify/document`) | **REAL**: la misma emergencia queda vinculada al paciente; sin coincidencia sigue NN |
| Identificar por huella, signos vitales, procedimientos, medicamentos | Mock (`MockParamedicApiService`) sobre el id **real** |
| Catálogo y huella | Mock (`MockParamedicCatalogService`, `MockFingerprintReaderService`, que alterna sin coincidencia / identificado) |

- **Reintentos:** un error de red deja la pantalla como estaba. El siguiente intento lleva el mismo `clientRequestId`,
  `startedAt` y motivo, así que si el servidor ya la había creado devuelve la misma emergencia (`200`).
- **Conflicto:** si el motivo cambió después de un fallo de resultado desconocido, el servidor responde `409`. La app
  muestra "La solicitud ya fue utilizada con datos diferentes. Vuelve a iniciar la creación de la emergencia." y
  **no** genera otra clave en silencio; para empezar de nuevo, sal de la pantalla y vuelve a entrar.
- **Identificación por documento:** solo Cédula (`NationalId`) y Pasaporte (`Passport`); el número va tal como se
  escribió (lo normaliza el servidor). Tiene su propio `clientRequestId`, que se conserva ante errores de red, `404` y
  `409`. Sin coincidencia: "No se encontró un paciente con ese documento." y se puede corregir, reintentar o continuar
  sin identificar. La huella sigue en mock.
- **Ubicación:** fuera de V1. La app no la toma ni la envía (`IDeviceLocationService` queda para cuando exista ese
  contrato).
- **Pendiente:** transferencia.

## Patient (estado actual)

| Pantalla | Qué hace | Contrato previsto |
|---|---|---|
| Acceso QR | Simula el escaneo; solo inicia el challenge | `POST /api/mobile/auth/qr/challenge` |
| Verificación facial | Simula la verificación 1:1 y crea la sesión | `POST /api/mobile/auth/face/verify` |
| Inicio | Resumen: último episodio, resultado y documento recientes; acceso al asistente | por definir |
| Mi perfil | Datos de la cuenta; foto y descripción como demostración local (solo este dispositivo) | `GET /api/mobile/auth/me` |
| Historial clínico | Alergias, condiciones, medicación habitual y episodios | por definir |
| Resultados | Resultados publicados | por definir |
| Documentos | Documentos autorizados; "Ver" responde "Disponible próximamente" | por definir |
| Asistente IA | Respuestas educativas fijas | por definir |

Datos simulados (no son datos clínicos reales): `MockPatientSessionService` (sesión en memoria de una hora, token de
prueba que nunca se persiste), `MockPatientDataService` (todos los datos de las pantallas) y
`MockPatientAssistantService`. `IPatientDataService` no recibe ningún identificador de paciente: el backend lo
deducirá de la sesión, así que la app no puede pedir datos de otra persona.

Reglas que se mantienen al conectar el backend:

- Nada de acceso directo a PostgreSQL, a los DbContext, a Azure Blob Storage ni a proveedores biométricos: todo pasa
  por el Mobile BFF.
- Los documentos se entregarán mediante el Mobile BFF, nunca con URLs permanentes.
- No se guardan plantillas ni fotos biométricas en el dispositivo; el token irá a SecureStorage, nunca a
  Preferences, que solo guarda la foto y la descripción locales del perfil de demostración.
- La sesión del paciente es independiente de la del personal clínico: nunca usa su access token.
- El asistente es educativo: no diagnostica, no prescribe y no sustituye a un profesional.

## Compilar

Con el SDK del `global.json` de la raíz y los workloads de MAUI:

```powershell
dotnet restore mobile/SCI-PH.Mobile.sln
dotnet build mobile/SCI-PH.Mobile/SCI-PH.Mobile.csproj -f net9.0-android
dotnet build mobile/SCI-PH.Mobile/SCI-PH.Mobile.csproj -f net9.0-windows10.0.19041.0
```

iOS y Mac Catalyst se compilan en Windows, pero ejecutarlos y publicarlos requiere macOS. El target Windows
muestra el aviso MVVMTK0045 de CommunityToolkit.Mvvm (`[ObservableProperty]` sobre campos); pasar a propiedades parciales exige
`LangVersion=preview` y se decidirá para toda la app.

## Tests

```powershell
dotnet test mobile/tests/SCI-PH.Mobile.Tests/SCI-PH.Mobile.Tests.csproj
```

La app tiene además el target `net9.0` (sin `OutputType=Exe`): no es una app ni se publica, solo compila la lógica
sin interfaz para que el proyecto de tests la referencie. Los tests usan fakes de `IAppNavigator`,
`IAppDialogService` y de los servicios de cada feature, sin Shell, ventanas ni handlers. La autenticación se prueba
con el cliente, el handler y el servicio reales contra un Mobile BFF en memoria (`Auth/FakeMobileBff`) y un
`ISecureStorage` falso. Sin tests de render XAML ni de dispositivo.

## CI

`.github/workflows/mobile.yml` (SCI-PH Mobile CI) corre en pull requests y pushes a `develop`/`main` solo cuando cambian
`mobile/**`, `global.json` o el propio workflow:

| Job | Runner | Qué hace |
|---|---|---|
| `mobile-tests` | ubuntu-latest | `dotnet test` de `SCI-PH.Mobile.Tests` (net9.0) con el workload `maui-android` |
| `mobile-android` | ubuntu-latest | Build Release de `net9.0-android`; sube el APK firmado con la clave de depuración como artefacto `sciph-mobile-android` (7 días) |
| `mobile-windows` | windows-latest | Build Release de `net9.0-windows10.0.19041.0` con el workload `maui-windows` |

Cada job fija su target con `-p:TargetFrameworks=…`, así no necesita los workloads de iOS/Mac Catalyst. No usa secretos,
no firma con claves propias y no publica nada.

## Pendiente

- QR y verificación facial reales del paciente (con el Rol 8).
- Conectar el resto del área paramédica (identificación por huella, signos vitales, procedimientos, medicamentos,
  traslado) al Mobile BFF; la apertura, la lectura y la identificación por documento ya son reales.
- Ubicación de la emergencia (contrato y almacenamiento por definir; ver ADR-008).
- Huella real (Device API), catálogos reales, pantalla de transferencia, modo sin conexión.
- Contratos e integración de historial, resultados, documentos y asistente del paciente.
- Converger PatientTheme con SciPhTheme.
- CI de iOS y Mac Catalyst (requiere runners macOS).
- Identidad de la app (`ApplicationId`) definitiva.
