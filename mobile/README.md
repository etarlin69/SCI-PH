# SCI-PH Mobile

Una sola aplicación .NET MAUI para el personal clínico prehospitalario y para los pacientes. Vive en su propia
solución, separada del backend:

| Solución | Contenido |
|---|---|
| `SCI-PH.sln` (raíz) | Backend: Domain, Application, Persistence, Infrastructure, BFFs, Device API y tests |
| `mobile/SCI-PH.Mobile.sln` | App MAUI `mobile/SCI-PH.Mobile/SCI-PH.Mobile.csproj` |

La app no referencia proyectos del backend: se comunicará con `SCI-PH.Mobile.BFF` (`/api/mobile`) por HTTP, según
[docs/api/mobile-bff-contracts.md](../docs/api/mobile-bff-contracts.md).

## Estructura

```text
mobile/
  SCI-PH.Mobile.sln
  SCI-PH.Mobile/
    App.xaml / App.xaml.cs        Recursos globales y ventana principal
    AppShell.xaml / .cs           Shell único; cada feature declara su raíz y registra sus rutas
    MauiProgram.cs                AddCoreServices() + AddParamedicFeature() (+ AddPatientFeature())
    Common/                       Compartido por todas las features
      Navigation/                 IAppNavigator (implementación sobre Shell)
      Dialogs/                    IAppDialogService
      Location/                   IDeviceLocationService (ubicación puntual de mejor esfuerzo)
      ViewModels/                 BaseViewModel (IsBusy, ErrorMessage, RunAsync)
    Extensions/                   AddCoreServices(): registro de lo común
    Features/
      Paramedic/                  Rol 6: emergencias prehospitalarias
      Patient/                    Rol 7: pendiente de integrar desde el PR #18
    Resources/                    Fuentes, iconos, splash y estilos (SciPhTheme)
    Platforms/                    Android, iOS, Mac Catalyst, Windows, Tizen
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

## Entrada a la aplicación (decisión para integrar el PR #18)

```text
SCI-PH Mobile
  ├─ Personal clínico (Paramedic) → inicio de sesión con usuario y contraseña
  └─ Paciente (Patient)           → acceso con QR + verificación facial 1:1
```

Ninguna de las dos autenticaciones está implementada todavía. Hoy la app abre directamente en "Nueva emergencia"
(Paramedic). Al integrar Patient se añadirá una entrada común que lleve a cada área; mientras no exista
autenticación real no se añade una pantalla de entrada simulada. El `AppShell` ya no depende de las páginas de
ninguna feature, de modo que Patient añade su raíz, sus elementos de menú y `RegisterPatientRoutes()` sin tocar
Paramedic.

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

## Compilar

Con el SDK del `global.json` de la raíz y los workloads de MAUI:

```powershell
dotnet restore mobile/SCI-PH.Mobile.sln
dotnet build mobile/SCI-PH.Mobile/SCI-PH.Mobile.csproj -f net9.0-android
dotnet build mobile/SCI-PH.Mobile/SCI-PH.Mobile.csproj -f net9.0-windows10.0.19041.0
```

iOS y Mac Catalyst requieren macOS. En Windows, el target Windows muestra el aviso MVVMTK0045 de
CommunityToolkit.Mvvm (`[ObservableProperty]` sobre campos); pasar a propiedades parciales exige
`LangVersion=preview` y se decidirá para toda la app.

## Pendiente

- Integrar `Features/Patient` desde el PR #18.
- Cliente HTTP del Mobile BFF, sesión con SecureStorage, refresh y manejo de 401.
- Huella real (Device API), catálogos reales, pantalla de transferencia, modo sin conexión.
- Proyecto de tests de ViewModels (requiere decidir cómo compilar la lógica fuera de los TFM de plataforma).
- CI móvil (pendiente con el Rol 9).
- Identidad de la app (`ApplicationId`) definitiva.
