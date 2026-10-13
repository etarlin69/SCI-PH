# SCI-PH Mobile

Una sola aplicación .NET MAUI para el personal clínico prehospitalario y para los pacientes. Vive en su propia
solución, separada del backend:

| Solución | Contenido |
|---|---|
| `SCI-PH.sln` (raíz) | Backend: Domain, Application, Persistence, Infrastructure, BFFs, Device API y tests |
| `mobile/SCI-PH.Mobile.sln` | App MAUI `mobile/SCI-PH.Mobile/SCI-PH.Mobile.csproj` y sus tests `mobile/tests/SCI-PH.Mobile.Tests` |

La app no referencia proyectos del backend: se comunicará con `SCI-PH.Mobile.BFF` (`/api/mobile`) por HTTP, según
[docs/api/mobile-bff-contracts.md](../docs/api/mobile-bff-contracts.md).

## Estructura

```text
mobile/
  SCI-PH.Mobile.sln
  SCI-PH.Mobile/
    App.xaml / App.xaml.cs        Recursos globales y ventana principal
    AppShell.xaml / .cs           Shell único: entrada compartida + área del paciente (menú con sesión)
    MauiProgram.cs                AddCoreServices() + AddParamedicFeature() + AddPatientFeature()
    Common/                       Compartido por todas las features
      Entry/                      EntryPage + EntryViewModel: elegir personal clínico o paciente
      Navigation/                 IAppNavigator (implementación sobre Shell), AppRoutes
      Dialogs/                    IAppDialogService
      Location/                   IDeviceLocationService (ubicación puntual de mejor esfuerzo)
      ViewModels/                 BaseViewModel (IsBusy/IsNotBusy, ErrorMessage, RunAsync)
    Extensions/                   AddCoreServices(): registro de lo común y de la entrada
    Features/
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
- Los servicios externos están detrás de interfaces. Hoy todos son mocks en memoria; al conectar el Mobile BFF solo
  cambian los registros.
- Los enums de los contratos se serializan por nombre (`JsonStringEnumConverter<T>`), así que sus valores numéricos
  no tienen que coincidir con los del Domain.

## Entrada a la aplicación

```text
SCI-PH Mobile (entrada compartida)
  ├─ Personal clínico (Paramedic) → futuro: inicio de sesión real   | hoy: entra directamente al prototipo
  └─ Paciente (Patient)           → QR → verificación facial 1:1 → sesión | hoy: los dos pasos son simulados
```

La app arranca en la entrada. **Ninguna autenticación es real todavía**: la entrada no pide usuario ni contraseña
y lo dice en pantalla. Los dos accesos se apilan sobre la entrada, así que "Atrás" permite volver a elegir.

- Personal clínico: Nueva emergencia y el resto del flujo paramédico, sin menú lateral.
- Paciente: el QR solo inicia el acceso (no autentica); la sesión se crea tras la verificación facial y abre el área
  del paciente. El menú lateral, con el nombre de la sesión, solo se habilita con sesión de paciente; "Cerrar
  sesión" la borra y vuelve a la entrada.

## Paramedic (estado actual)

| Pantalla | Qué hace |
|---|---|
| Nueva emergencia | Motivo de consulta; ubicación puntual si hay permiso. Reintentos con el mismo `clientRequestId`. |
| Identificar paciente | Huella, documento o continuar como paciente no identificado (NN), las tres desde el inicio. |
| Signos vitales | Tomas con límites de plausibilidad del Domain (`VitalSignLimits`); no son umbrales clínicos. |
| Procedimientos | Registro desde el catálogo. |
| Medicamentos | Registro de lo ya administrado (SCI-PH no prescribe). |

Datos simulados: `MockParamedicApiService` (emergencias en memoria con idempotencia), `MockParamedicCatalogService`
y `MockFingerprintReaderService` (alterna sin coincidencia / identificado). Pendiente: transferencia.

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
`IAppDialogService` y de los servicios de cada feature, sin Shell, ventanas ni handlers. Sin tests de render XAML,
de dispositivo ni de integración con el Mobile BFF por ahora.

## Pendiente

- Inicio de sesión real del personal clínico; QR y verificación facial reales del paciente (con el Rol 8).
- Cliente HTTP del Mobile BFF, sesión con SecureStorage, refresh, manejo de 401 y protección de rutas.
- Huella real (Device API), catálogos reales, pantalla de transferencia, modo sin conexión.
- Contratos e integración de historial, resultados, documentos y asistente del paciente.
- Converger PatientTheme con SciPhTheme.
- CI móvil (pendiente con el Rol 9).
- Identidad de la app (`ApplicationId`) definitiva.
