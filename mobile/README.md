# SCI-PH Mobile

Una sola aplicación .NET MAUI para el personal clínico prehospitalario y para los pacientes. Vive en su propia
solución, separada del backend:

| Solución | Contenido |
|---|---|
| `SCI-PH.sln` (raíz) | Backend: Domain, Application, Persistence, Infrastructure, BFFs, Device API y tests |
| `mobile/SCI-PH.Mobile.sln` | App MAUI `mobile/SCI-PH.Mobile/SCI-PH.Mobile.csproj` y sus tests `mobile/tests/SCI-PH.Mobile.Tests` |

La app no referencia proyectos del backend: se comunica con `SCI-PH.Mobile.BFF` (`/api/mobile`) por HTTP, según
[docs/api/mobile-bff-contracts.md](../docs/api/mobile-bff-contracts.md), con DTOs propios.
Hoy son reales el acceso del personal clínico (`api/mobile/auth`) y la apertura, lectura, identificación por documento
y signos vitales de la emergencia (`api/mobile/emergencies`); el resto sigue simulado.

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
| Signos vitales | Una toma **real** de 1 a 7 valores con los límites de plausibilidad del Domain (`VitalSignLimits`; no son umbrales clínicos). Registrada → Procedimientos. |
| Procedimientos | Un procedimiento **real** ya realizado, elegido del catálogo fijo del servidor, con su resultado opcional. Registrado → Medicamentos. |
| Medicamentos | Una dosis **real** ya administrada (SCI-PH no prescribe), del catálogo de medicamentos activos. Registrada → confirmación y formulario listo para otra dosis. "Continuar" → Observaciones. |
| Observaciones | Una observación **real** (nota `Prehospital`, hasta 4000 caracteres en la app) con hora automática. Registrada → confirmación y editor listo para otra. "Continuar" → Traslado. |
| Traslado | Destino (clínicas activas), solicitar, iniciar y cancelar el traslado **real**. En traslado se vuelve a las pantallas clínicas para seguir registrando. El estado se restaura desde el servidor. La recepción la hace el hospital. |

Se entra con la sesión real del personal clínico. `HttpParamedicApiService` es híbrido:

| Operación | Origen |
|---|---|
| Crear emergencia (`POST /api/mobile/emergencies`) | **REAL** (cliente autenticado, `IMobileApiClient`) |
| Leer emergencia (`GET /api/mobile/emergencies/{id}`) | **REAL** |
| Identificar por documento (`POST /api/mobile/emergencies/{id}/identify/document`) | **REAL**: la misma emergencia queda vinculada al paciente; sin coincidencia sigue NN |
| Registrar signos vitales (`POST /api/mobile/emergencies/{id}/vital-signs`) | **REAL**: una toma, todo o nada, sobre la misma emergencia |
| Registrar un procedimiento (`POST /api/mobile/emergencies/{id}/procedures`) | **REAL**: un procedimiento ya realizado sobre la misma emergencia |
| Catálogo de procedimientos (`GET /api/mobile/catalogs/procedures`) | **REAL** (`HttpParamedicCatalogService`): catálogo fijo del servidor (`SCI-PH-PREHOSP`) |
| Registrar un medicamento administrado (`POST /api/mobile/emergencies/{id}/medication-administrations`) | **REAL**: una dosis, sin prescripción, sobre la misma emergencia |
| Catálogo de medicamentos (`GET /api/mobile/catalogs/medications`) | **REAL** (`HttpParamedicCatalogService`): solo medicamentos activos |
| Registrar una observación (`POST /api/mobile/emergencies/{id}/notes`) | **REAL**: una nota `Prehospital` sobre la misma emergencia |
| Traslado (`POST …/transfers`, `…/transfers/{transferId}/start`, `…/cancel`) y destinos (`GET /api/mobile/catalogs/transfer-destinations`) | **REAL**: solicitar, iniciar y cancelar sobre un traslado concreto |
| Identificar por huella | Mock (`MockParamedicApiService`, `MockFingerprintReaderService`, que alterna sin coincidencia / identificado) sobre el id **real** |

- **Reintentos:** un error de red deja la pantalla como estaba. El siguiente intento lleva el mismo `clientRequestId`,
  `startedAt` y motivo, así que si el servidor ya la había creado devuelve la misma emergencia (`200`).
- **Conflicto:** si el motivo cambió después de un fallo de resultado desconocido, el servidor responde `409`. La app
  muestra "La solicitud ya fue utilizada con datos diferentes. Vuelve a iniciar la creación de la emergencia." y
  **no** genera otra clave en silencio; para empezar de nuevo, sal de la pantalla y vuelve a entrar.
- **Identificación por documento:** solo Cédula (`NationalId`) y Pasaporte (`Passport`); el número va tal como se
  escribió (lo normaliza el servidor). Tiene su propio `clientRequestId`, que se conserva ante errores de red, `404` y
  `409`. Sin coincidencia: "No se encontró un paciente con ese documento." y se puede corregir, reintentar o continuar
  sin identificar. La huella sigue en mock.
- **Signos vitales:** cada toma tiene su propio `clientRequestId` y su hora (`recordedAt`, UTC con offset), generados
  al primer intento de guardar y conservados ante errores de red, respuestas perdidas, `400`, `403` y `409`; solo
  viajan los valores capturados (la unidad la pone el servidor). Registrada (`201`, o `200` si era un reintento) →
  Procedimientos. Temperatura con hasta 2 decimales.
- **Procedimientos:** solo se elige un código del catálogo del servidor (el nombre y la descripción los pone el
  servidor; no hay texto libre como nombre). Cada procedimiento tiene su propio `clientRequestId` y su hora
  (`performedAt`, UTC con offset), generados al primer intento de guardar y conservados ante errores de red, respuestas
  perdidas, `400`, `403`, `404` y `409`. El resultado (`outcome`, hasta 4000 caracteres) es opcional y no se envía si
  está vacío. Registrado (`201`, o `200` si era un reintento) → Medicamentos.
- **Medicamentos:** se elige un medicamento activo del catálogo (nombre, presentación y concentración) y viaja su id. Su
  vía por defecto se preselecciona y se puede cambiar; la vía viaja como nombre de `MedicationRoute` (13 valores, con
  etiqueta en español solo en pantalla). La dosis acepta coma o punto y se valida como el servidor (`> 0`, máximo 3
  decimales, menor que 1 000 000 000). La unidad se elige de una lista de ayuda (mg, mcg, g, mL, UI, gotas,
  inhalaciones) o se escribe con "Otra" (1 a 24 caracteres); el servidor acepta cualquier unidad válida. Cada dosis tiene
  su propio `clientRequestId` y su hora (`administeredAt`), conservados ante errores de red, respuestas perdidas, `400`,
  `403`, `404` y `409`. Registrada (`201`, o `200` si era un reintento): confirmación, formulario limpio y nueva clave
  para la siguiente dosis, sin salir de la pantalla. "Continuar a observaciones" lleva a Observaciones.
- **Observaciones:** texto libre obligatorio (hasta 4000 caracteres en la app; el servidor admite 8000) con contador. El
  tipo (`Prehospital`) y el autor los pone el servidor. La hora (`occurredAt`) es automática y no editable: se fija en el
  primer intento de guardar, se muestra como solo lectura y se conserva, con su `clientRequestId`, ante errores de red,
  respuestas perdidas, `400`, `403`, `404` y `409`. Registrada (`201`, o `200` si era un reintento): confirmación, la nota
  en la lista de la sesión, editor limpio y nueva clave y hora para la siguiente. Las notas no se editan: una corrección
  es otra nota. "Continuar" lleva al Traslado.
- **Traslado:** se elige una clínica activa (nombre, tipo y lugar) y se solicita con su `clientRequestId`, conservado en
  los reintentos. "Iniciar traslado" y "Cancelar traslado" actúan sobre ese traslado concreto y repetirlos es seguro. En
  traslado, la atención sigue a cargo del paramédico: la pantalla ofrece volver a signos vitales, procedimientos,
  medicamentos y observaciones. Cada vez que se abre, la pantalla se reconstruye con el `activeTransfer` de la
  emergencia. Cuando el hospital recibe al paciente, la app ya no puede registrar (`409`). No hay botón de recepción.
- **Ubicación:** fuera de V1. La app no la toma ni la envía (`IDeviceLocationService` queda para cuando exista ese
  contrato).
- **Pendiente:** cierre del traslado (`Complete`) y llegada física como evento propio.

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
- Conectar la identificación por huella al Mobile BFF; el resto del área paramédica (apertura, lectura, identificación
  por documento, signos vitales, procedimientos, medicamentos, observaciones y traslado) ya es real.
- Ubicación de la emergencia (contrato y almacenamiento por definir; ver ADR-008).
- Huella real (Device API), modo sin conexión, cierre del traslado (`Complete`) y llegada física.
- Contratos e integración de historial, resultados, documentos y asistente del paciente.
- Converger PatientTheme con SciPhTheme.
- CI de iOS y Mac Catalyst (requiere runners macOS).
- Identidad de la app (`ApplicationId`) definitiva.
