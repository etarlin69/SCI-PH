# SCI-PH Mobile

Una sola aplicación .NET MAUI para el personal clínico prehospitalario y para los pacientes. Vive en su propia
solución, separada del backend:

| Solución | Contenido |
|---|---|
| `SCI-PH.sln` (raíz) | Backend: Domain, Application, Persistence, Infrastructure, BFFs, Device API y tests |
| `mobile/SCI-PH.Mobile.sln` | App MAUI `mobile/SCI-PH.Mobile/SCI-PH.Mobile.csproj` y sus tests `mobile/tests/SCI-PH.Mobile.Tests` |

La app no referencia proyectos del backend: se comunica con `SCI-PH.Mobile.BFF` (`/api/mobile`) por HTTP, según
[docs/api/mobile-bff-contracts.md](../docs/api/mobile-bff-contracts.md), con DTOs propios.
Hoy son reales el inicio de sesión del personal clínico y de los pacientes (`api/mobile/auth`), toda el área paramédica
(`api/mobile/emergencies`) y el expediente propio del paciente (`api/mobile/patient/me`); Documentos todavía no está
disponible y el asistente de IA del paciente no está en la app.

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
      Authentication/             Sesión de la cuenta (personal clínico o paciente): MobileAuthService,
                                  ClinicalSessionService (memoria, con su AccountArea), ISecureTokenStore (SecureStorage),
                                  contratos de api/mobile/auth
      Startup/                    StartupPage: restaura la sesión guardada antes de mostrar nada
      Entry/                      EntryPage + EntryViewModel: un solo "Iniciar sesión"
      Navigation/                 IAppNavigator (implementación sobre Shell), AppRoutes
      Dialogs/                    IAppDialogService
      Location/                   IDeviceLocationService (ubicación puntual de mejor esfuerzo)
      ViewModels/                 BaseViewModel (IsBusy/IsNotBusy, ErrorMessage, RunAsync)
    Extensions/                   AddCoreServices(): registro de lo común y de la entrada
    Features/
      Auth/                       Login y MFA (personal clínico y pacientes); cierre de sesión (ClinicalAccountViewModel)
      Paramedic/                  Rol 6: emergencias prehospitalarias
      Patient/                    Rol 7: área del paciente con su propio expediente (HttpPatientDataService)
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
- Los servicios externos están detrás de interfaces y usan `IMobileApiClient` (nunca `HttpClient` en un ViewModel).
  El área del paciente ya no usa mocks (sus fakes viven solo en los tests); en el área paramédica,
  `HttpParamedicApiService` todavía usa `MockParamedicApiService` internamente.
- Los enums de los contratos se serializan por nombre (`JsonStringEnumConverter<T>`), así que sus valores numéricos
  no tienen que coincidir con los del Domain.

## Entrada a la aplicación

```text
Arranque ─┬─ refresh token guardado → refresh + /me → área de la cuenta (paramédica o del paciente)
          │                            └─ rechazado o sin área → se borra → entrada
          └─ sin token o sin conexión → entrada

Entrada compartida → Iniciar sesión → login (Mobile BFF) → [MFA] → /me
  ├─ permiso Emergencies.Operate → área paramédica
  ├─ rol Patient (sin permisos) → área del paciente
  └─ ninguna de las dos (o ambas) → sin acceso: la sesión se revoca y se borra
```

Un solo inicio de sesión para todos ([ADR-015](../docs/adr/ADR-015-patient-authentication-qr-identification.md)):
**el QR y el rostro nunca inician una sesión**. El login se apila sobre la entrada, así que "Atrás" vuelve a ella.

- Personal clínico: Nueva emergencia y el resto del flujo paramédico, sin menú lateral, con "Cerrar sesión" en la barra
  de Nueva emergencia.
- Paciente: la cuenta que activó con la invitación de su centro de salud (no hay registro en la app). Entra a su área;
  el menú lateral solo se habilita con una sesión de paciente y "Cerrar sesión" la cierra y vuelve a la entrada.

## Inicio de sesión (personal clínico y pacientes)

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
- **Área**: toda sesión nueva (login, MFA, restauración) llama a `/me` y decide el área (`ClinicalUser.Area`): el
  permiso `Emergencies.Operate` abre el área paramédica (el personal nunca se decide por rol) y el rol `Patient`, que
  no tiene permisos, el área del paciente. Sin área (o con las dos a la vez, que el servidor no permite) la sesión se
  revoca en el servidor, se borra y se muestra "Tu cuenta no tiene acceso a la app móvil de SCI-PH.". El paciente no
  recibe `Emergencies.Operate`; el servidor vuelve a autorizar cada petición.
- **Peticiones protegidas** (`IMobileApiClient`): `AuthenticatedHttpHandler` añade `Authorization: Bearer`. Ante un
  401 hace un único refresh (single-flight: con varias peticiones a la vez solo una llama a `/refresh`), guarda el
  refresh token rotado y repite la petición una vez. Si la repetición vuelve a dar 401, o el refresh es rechazado
  (caducado, revocado o reutilizado), se borra la sesión y la app vuelve a la entrada. Login, MFA, refresh y logout
  usan un cliente sin ese handler, así que un refresh nunca dispara otro.
- **Arranque**: con refresh token guardado se renueva y se valida con `/me` antes de mostrar el área de la cuenta. Si el
  servidor lo rechaza, se borra; si `/refresh` no tiene conexión, se conserva para el siguiente arranque y se muestra la
  entrada. Si `/refresh` funciona pero `/me` falla, el token ya rotado se revoca (si hay conexión) y se borra: no queda
  una sesión a medias y hay que volver a iniciar sesión.
- **Logout**: revoca el refresh token en el servidor si hay conexión y siempre borra la sesión local. Hay una sola
  sesión en la app, sea de personal o de paciente.
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
| Identificar paciente | Huella **real** (lector elegido, lectura, candidato y confirmación), documento (cédula o pasaporte) o continuar como paciente no identificado (NN), las tres desde el inicio. |
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
| Identificar por huella (`GET /api/mobile/devices/fingerprint-readers`, `…/identify/fingerprint/sessions`, `…/{sessionId}`, `…/confirm`, `…/cancel`) | **REAL** (`HttpFingerprintIdentificationService`): la app abre una lectura con el lector elegido, consulta su estado y confirma el candidato; nunca genera ni envía un id de paciente |

- **Reintentos:** un error de red deja la pantalla como estaba. El siguiente intento lleva el mismo `clientRequestId`,
  `startedAt` y motivo, así que si el servidor ya la había creado devuelve la misma emergencia (`200`).
- **Conflicto:** si el motivo cambió después de un fallo de resultado desconocido, el servidor responde `409`. La app
  muestra "La solicitud ya fue utilizada con datos diferentes. Vuelve a iniciar la creación de la emergencia." y
  **no** genera otra clave en silencio; para empezar de nuevo, sal de la pantalla y vuelve a entrar.
- **Identificación por documento:** solo Cédula (`NationalId`) y Pasaporte (`Passport`); el número va tal como se
  escribió (lo normaliza el servidor). Tiene su propio `clientRequestId`, que se conserva ante errores de red, `404` y
  `409`. Sin coincidencia: "No se encontró un paciente con ese documento." y se puede corregir, reintentar o continuar
  sin identificar.
- **Identificación por huella:** la app NO habla con el ESP32/R307. Se elige un lector activo (si hay uno solo, queda
  elegido) y "Iniciar lectura" abre una sesión con su propio `clientRequestId` (conservado en los reintentos). Mientras
  el lector captura, la app consulta la sesión cada segundo y deja de hacerlo al cambiar de estado o al salir de la
  pantalla (se reanuda al volver). Con coincidencia muestra solo nombre y fecha de nacimiento, y la emergencia se
  identifica **solo al confirmar** (con su propio `clientRequestId`). Sin coincidencia, lectura insuficiente, lector no
  disponible o lectura vencida (90 s), lo dice y el documento y continuar como NN siguen disponibles; sin conexión al
  consultar, sigue intentando hasta que la lectura vence. Identificar por documento o continuar como NN cancela una
  lectura en curso.
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

## Patient

Expediente **propio** del paciente autenticado, **solo lectura**, con `HttpPatientDataService` sobre
`api/mobile/patient/me/...` ([contrato](../docs/api/mobile-bff-contracts.md#autoservicio-del-paciente--implementado)).
La app **nunca envía un id de paciente**: el servidor lo deduce de la cuenta (`Patient.IdentityUserId`).

| Pantalla | Qué muestra | Contrato |
|---|---|---|
| Inicio | Nombre, resultados publicados y atenciones; última atención y último resultado | `GET …/patient/me/summary` |
| Mi perfil | Nombre, documento (enmascarado), email, sexo, fecha de nacimiento, teléfono y conteos; solo lectura | `GET …/patient/me` + `…/summary` |
| Historial clínico | Alergias (última versión del historial), diagnósticos, recetas emitidas y atenciones | `GET …/allergies`, `…/diagnoses`, `…/prescriptions`, `…/encounters` |
| Resultados | Resultados publicados (finales o corregidos) con valor, tipo y rango de referencia | `GET …/patient/me/results` |
| Documentos | "Todavía no disponible": no hay almacenamiento ni descarga de documentos | — |

- **Errores**: 401 tras renovar la sesión → "Tu sesión terminó. Inicia sesión de nuevo." (la app vuelve a la
  entrada); 403 (cuenta sin expediente vinculado) → "Tu cuenta no tiene acceso a un expediente clínico en SCI-PH.
  Contacta a tu centro de salud."; 404 → "La información solicitada no está disponible."; sin conexión → el mensaje
  común de red. Un expediente vacío es un estado vacío, no un error. **Nunca hay datos simulados de respaldo**: si
  algo falla, la pantalla queda vacía con el mensaje.
- **Mi QR** ([ADR-015](../docs/adr/ADR-015-patient-authentication-qr-identification.md)): identifica al paciente ante el
  personal autorizado y **nunca inicia sesión ni da acceso por sí solo**. El contenido lo emite el servidor
  (`POST api/mobile/patient/me/qr`), vale 15 minutos, vive **solo en memoria** y se dibuja en el dispositivo. Sin red no hay QR y
  nunca se muestra uno vencido. Al cerrar la sesión o cambiar de cuenta se borra, sin revocarlo en el servidor. Se dibuja
  con el generador de `ZXing.Net.Maui.Controls` 0.6.0 (negro sobre blanco, 280 × 280, con su propia zona de silencio de al
  menos 4 módulos). La app registra ZXing con `UseBarcodeReader()`, pero el área del paciente **nunca crea la cámara
  ni pide su permiso**: solo lo hace el escáner del paramédico.
- **Fuera de la app por ahora**: descarga de documentos, citas, notificaciones, edición del perfil y el asistente de IA (se quitó el chat
  simulado: no se muestran respuestas médicas falsas como reales).

Reglas:

- Nada de acceso directo a PostgreSQL, a los DbContext, a Azure Blob Storage ni a proveedores biométricos: todo pasa
  por el Mobile BFF.
- Los documentos se entregarán mediante el Mobile BFF, nunca con URLs permanentes.
- No se guardan plantillas ni fotos biométricas en el dispositivo. El refresh token está en SecureStorage y Android no
  respalda los datos de la app (`allowBackup="false"`). El área del paciente no guarda datos en el dispositivo: el perfil
  es de solo lectura, y lo que versiones anteriores dejaron (foto y descripción de demostración) se borra al arrancar y al
  cerrar la sesión.
- Las pantallas del paciente se vacían al cerrar la sesión o cambiar de cuenta (`PatientRecordViewModel`) y descartan una
  respuesta que llega tarde: el paciente B nunca ve datos del paciente A.
- No se cachean datos clínicos en el dispositivo: cada pantalla los pide al abrirse.

## Escáner del QR del paciente (paramédico)

QR #2 de [ADR-015](../docs/adr/ADR-015-patient-authentication-qr-identification.md): una vía más en "Identificar paciente",
junto a huella, documento y NN ("Código QR del paciente" → "Escanear QR"), solo mientras la emergencia se puede identificar.

| Paso | Qué ocurre | Contrato |
|---|---|---|
| Permiso | Al entrar, se consulta; si falta, se pide **una vez**. Denegado: explicación y "Intentar de nuevo" (solo a pedido). Denegado sin volver a preguntar: "Abrir configuración". Sin cámara: "Cámara no disponible." | — |
| Lectura | Solo `QR_CODE`. La primera lectura gana (comparación atómica en el hilo de la cámara) y la detección se apaga en el acto. Un código sin el prefijo `sciphqr:v1:` muestra "Código no válido." y no llega al servidor. El contenido nunca se recorta ni se modifica. | — |
| Candidato | Nombre, fecha de nacimiento y documento enmascarado por el servidor; nada se identifica. "Confirmar paciente", "Escanear nuevamente" o "Cancelar". | `POST …/emergencies/{id}/identify/qr/resolve` |
| Confirmación | Solo al pulsar "Confirmar paciente". Una clave por candidato, la misma en cada reintento (sin respuesta, 503, 429). Mientras se confirma, Atrás no sale de la página ni cancela la petición (la salida al vencer la sesión sí ocurre). Al terminar vuelve a la identificación, que lee la emergencia del servidor (misma atención y código NN) y suelta una lectura de huella pendiente. | `POST …/emergencies/{id}/identify/qr/confirm` |

- **Errores:** 404 (vencido, sustituido, revocado, desconocido: no se distingue) → "El código no es válido o ya expiró. Pide
  al paciente actualizar su QR.", se borra el candidato y se vuelve a leer tras una pausa; el mismo código rechazado no se
  reenvía mientras siga delante de la cámara (solo se guarda su hash), pero uno distinto, como el QR renovado, sí. 409 → se lee la emergencia: con
  paciente, "La emergencia ya está identificada." y nunca se sustituye; sin él, un conflicto genérico. 429 → escáner
  detenido con el tiempo de `Retry-After`, se reanuda a pedido. 403 → mensaje genérico. 401 → el refresh común
  (`AuthenticatedHttpHandler`), sin lógica propia.
- **Cámara:** la vista de ZXing solo existe con la página visible, la app en primer plano y el estado de lectura; con el
  candidato, al confirmar, al salir o en segundo plano se desconecta. Al volver a primer plano nada se confirma solo.
- **Sesión:** salir de la página o cambiar de cuenta descarta las respuestas tardías y borra candidato, contenido y clave.
- **Plataformas:** Android (`CAMERA`, cámara no obligatoria) e iOS (`NSCameraUsageDescription`) piden el permiso del sistema;
  Windows usa la cámara de ZXing y, si no hay cámara o la privacidad la bloquea, muestra "Cámara no disponible."; en
  Mac Catalyst el escáner no se ofrece y no se declara la cámara. El APK es uno para paciente y paramédico: el permiso se
  declara una vez, pero solo el escáner lo pide.

**Validación (2026-11-05):**

| Nivel | Estado |
|---|---|
| Código | Tests sin interfaz del escáner (`Paramedic/QrScannerTests`); builds Release de Android y Windows |
| Cámara: emulador Android (`pixel_7_-_api_35`, build Debug) | **Provisional.** Lecturas reales por la cámara del emulador (`virtualscene`), con el QR de "Mi QR" como póster de la escena: flujo completo y casos de abajo |
| Cámara: Windows (webcam integrada, build Debug) | La vista en vivo, la liberación al minimizar o salir y la reactivación al restaurar funcionan. **Lectura real de un QR: no realizada** (la webcam no puede ver la pantalla del propio equipo; hace falta mostrarle el código) |
| Dispositivo físico | **No realizado: no hay un Android físico disponible.** Requisito antes de publicar |
| iOS y Mac Catalyst | Solo compila el C# desde Windows, sin paquete de la app ni ejecución (requiere macOS) |

Comprobado con el emulador contra un Mobile BFF real sobre una base desechable:

- Flujo: "Mi QR" → NN → permiso → lectura → una sola resolución → candidato sin identificar → "Confirmar paciente" →
  identificada con el mismo código NN → signos vitales, procedimiento, medicamento, observación y traslado iniciado.
- Rechazos: QR vencido a los 15 minutos reales, QR anterior tras reemitir (también con su candidato en pantalla al
  confirmar), `sciphqr:v1:` inventado (mismo mensaje), código ajeno (sin llamada), Code 128 (ignorado).
- 409 real ("La emergencia ya está identificada.", sin sustituir ni identificar de nuevo); 429 real con un límite bajo por
  configuración (cámara detenida, sin reintentos solos); cancelar deja la emergencia en NN.
- Permiso: solo se pide en el escáner, nunca en "Mi QR"; denegación, "Intentar de nuevo", denegación permanente,
  "Abrir configuración" y vuelta con el permiso concedido. Segundo plano: la cámara se libera y nada se confirma al volver.
- Atrás mientras se confirma (respuesta retenida 12 s por un proxy de prueba): el botón del sistema y el de la barra no sacan
  de la página, la confirmación no se cancela y la emergencia solo se lee tras la respuesta. Con 503 y con respuesta perdida
  tras aplicarse, la misma clave y una sola identificación. Si la sesión vence mientras se confirma, la app sí vuelve a la entrada.
- "Mi QR" (emulador y Windows): visible, sin recortes, negro sobre blanco, legible por dos decodificadores y por la cámara,
  con cuenta atrás, y el contenido coincide con la credencial activa del servidor. `BarcodeMargin = 4` (ZXing lo cuenta en
  módulos): la propia imagen lleva al menos 4 módulos de blanco, unos 6,5 a 6,8 a 280 × 280 por el redondeo de la escala. La
  cámara del emulador lo leyó recortado al símbolo más 4 módulos. En Windows: renovación real al vencer, error de red,
  "Reintentar" y borrado al cerrar sesión.

Sin comprobar en ejecución: "Cámara no disponible." en Windows (sin cámara o bloqueada por privacidad), el APK Release en
ejecución (exige HTTPS) y la lectura con una cámara y una luz reales.

Prueba pendiente: mostrar "Mi QR" a la webcam de Windows y a la cámara de un Android físico → candidato → confirmar.

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
`ISecureStorage` falso; el área del paciente, con `HttpPatientDataService` real contra `Patient/FakePatientSelfBff`; el escáner de QR, con `HttpParamedicApiService` real
contra `Paramedic/FakeQrIdentificationBff` y un permiso de cámara falso (la página entrega lo leído: sin cámara). Sin tests
de render XAML ni de dispositivo.

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

- Lectura del QR con una cámara física (ver
  [Escáner del QR del paciente](#escáner-del-qr-del-paciente-paramédico)): probada solo con la cámara virtual del emulador;
  un Android físico es requisito antes de publicar.
- Todo el área paramédica (apertura, lectura, identificación por documento y por huella, signos vitales,
  procedimientos, medicamentos, observaciones y traslado) ya es real.
- Ubicación de la emergencia (contrato y almacenamiento por definir; ver ADR-008).
- Enrolamiento de huellas, firmware real del ESP32/R307, modo sin conexión, cierre del traslado (`Complete`) y llegada
  física.
- Documentos del paciente (con la vertical de almacenamiento de documentos) y su asistente de IA.
- Historial del paciente paginado (hoy, las 200 entradas más recientes de cada lista; P1).
- Integración HTTP completa del área paramédica: `HttpParamedicApiService` aún usa `MockParamedicApiService`.
- Converger PatientTheme con SciPhTheme.
- CI de iOS y Mac Catalyst (requiere runners macOS).
- Identidad de la app (`ApplicationId`) definitiva.
