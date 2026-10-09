# SCI-PH — Prototipo Mobile Patient

## Rol 7 — Aplicación móvil del paciente

Este directorio contiene el prototipo independiente desarrollado para el **Rol 7 — Mobile / Patient** del proyecto SCI-PH.

El objetivo de este prototipo es desarrollar y validar la experiencia móvil del paciente antes de integrarla en el proyecto MAUI compartido `SCI-PH.Mobile`.

> Este proyecto es un prototipo funcional e independiente.
> No representa una segunda aplicación móvil definitiva.
> El código del paciente deberá integrarse posteriormente con el proyecto MAUI compartido junto al módulo desarrollado por el Rol 6.

---

# Estado actual

El prototipo actualmente compila correctamente en:

- Android
- Windows
- iOS
- MacCatalyst

El desarrollo se encuentra en una etapa de interfaz, navegación y servicios simulados.

Actualmente no existe conexión real con el backend de SCI-PH.

---

# Tecnologías utilizadas

- .NET 9
- .NET MAUI
- C#
- XAML
- Shell Navigation
- Dependency Injection
- Servicios Mock
- Arquitectura modular por Features

El proyecto utiliza un `global.json` para trabajar con .NET 9.

---

# Estructura del proyecto

```text
SCI-PH.PatientPrototype/
│
├── App.xaml
├── App.xaml.cs
│
├── AppShell.xaml
├── AppShell.xaml.cs
│
├── MauiProgram.cs
├── SCI-PH.PatientPrototype.csproj
│
├── Features/
│   └── Patient/
│       │
│       ├── Auth/
│       │   ├── QrScanPage.xaml
│       │   ├── QrScanPage.xaml.cs
│       │   ├── FaceVerifyPage.xaml
│       │   └── FaceVerifyPage.xaml.cs
│       │
│       ├── Home/
│       │   ├── HomePage.xaml
│       │   └── HomePage.xaml.cs
│       │
│       ├── Profile/
│       │   ├── ProfilePage.xaml
│       │   ├── ProfilePage.xaml.cs
│       │   └── patient_avatar.png
│       │
│       ├── History/
│       │   ├── HistoryPage.xaml
│       │   └── HistoryPage.xaml.cs
│       │
│       ├── Results/
│       │   ├── ResultsPage.xaml
│       │   └── ResultsPage.xaml.cs
│       │
│       ├── Documents/
│       │   ├── DocumentsPage.xaml
│       │   └── DocumentsPage.xaml.cs
│       │
│       ├── Ai/
│       │   ├── AiAssistantPage.xaml
│       │   └── AiAssistantPage.xaml.cs
│       │
│       ├── Models/
│       │   └── PatientSessionDto.cs
│       │
│       └── Services/
│           ├── Interfaces/
│           │   └── IPatientSessionService.cs
│           │
│           └── Mocks/
│               └── MockPatientSessionService.cs
│
├── Platforms/
├── Resources/
├── Properties/
└── global.json
```

---

# Funcionalidades implementadas

## 1. Pantalla de acceso mediante QR

Se creó una pantalla inicial para representar el acceso del paciente mediante código QR.

Actualmente el QR es una **simulación visual**.

Flujo esperado:

```text
Abrir aplicación
       ↓
Escanear / validar QR
       ↓
Challenge de autenticación
       ↓
Verificación facial
```

Actualmente el prototipo simula el paso del QR hacia la verificación facial.

En la implementación final este flujo deberá consumir el Mobile BFF.

Contrato previsto:

```http
POST /api/mobile/auth/qr/challenge
```

---

# 2. Verificación facial

Se creó una pantalla de verificación facial que representa el proceso de verificación biométrica 1:1.

Actualmente:

- La captura facial es simulada.
- La validación facial es simulada.
- No se utiliza proveedor biométrico real.
- No se almacenan plantillas faciales.
- No se almacenan fotografías biométricas localmente.

Después de una verificación simulada correcta se crea una sesión mock.

Contrato previsto:

```http
POST /api/mobile/auth/face/verify
```

La implementación real deberá ser proporcionada/integrada utilizando los servicios correspondientes del backend y del Rol 8.

---

# 3. Sesión simulada del paciente

Se agregó:

```text
IPatientSessionService
```

con una implementación:

```text
MockPatientSessionService
```

También se creó:

```text
PatientSessionDto
```

La sesión simulada contiene actualmente:

- Estado de autenticación.
- ID del paciente.
- Nombre del paciente.
- Token simulado.
- Fecha de expiración.

Ejemplo conceptual:

```text
QR
 ↓
Rostro
 ↓
CreateMockSessionAsync()
 ↓
Paciente autenticado
```

El token utilizado actualmente es únicamente de prueba.

No representa un JWT real.

---

# 4. Inicio del paciente

Se desarrolló una pantalla Home para presentar un resumen de la información del paciente.

La interfaz incluye elementos visuales para:

- Identidad del paciente.
- Información resumida.
- Últimos datos clínicos.
- Resultados recientes.
- Documentos.
- Acceso al asistente IA.

Actualmente los datos son simulados.

---

# 5. Perfil del paciente

Se creó una pantalla de perfil con información visual del paciente.

Incluye:

- Avatar.
- Información personal.
- Descripción.
- Información general.
- Elementos editables utilizados para probar la experiencia de usuario.

La pantalla actualmente trabaja con información local/mock.

Contrato previsto para la información real:

```http
GET /api/mobile/me
```

---

# 6. Historial clínico

Se desarrolló una interfaz de historial clínico para representar información como:

- Antecedentes.
- Condiciones médicas.
- Alergias.
- Medicamentos.
- Episodios clínicos.

Actualmente se muestran datos simulados.

Ejemplos utilizados durante el desarrollo:

- Alergia a penicilina.
- Hipertensión.
- Losartán.
- Episodios médicos simulados.

---

# 7. Resultados

Se creó una pantalla para visualizar resultados clínicos.

Actualmente incluye datos de demostración como:

- Hemograma.
- Estado del resultado.
- Glucosa.
- Valores clínicos simulados.

Los datos todavía no provienen del backend.

---

# 8. Documentos

Se desarrolló una pantalla para representar documentos autorizados del paciente.

Ejemplos:

- Resumen de alta.
- Resultado de laboratorio.

Actualmente los documentos son simulados.

En la arquitectura final:

- La aplicación no deberá recibir URLs permanentes.
- Los documentos deberán ser entregados de forma segura mediante el Mobile BFF.
- La aplicación móvil no deberá acceder directamente a Azure Blob Storage.

---

# 9. Asistente SCI-PH AI

Se creó una interfaz para el asistente educativo SCI-PH.

El objetivo del asistente es:

- Explicar información médica en lenguaje comprensible.
- Proporcionar contenido educativo.
- Ayudar al paciente a entender información disponible en el sistema.

El asistente:

- NO debe diagnosticar.
- NO debe prescribir.
- NO debe sustituir a un profesional de salud.

Actualmente la conversación del asistente es simulada.

---

# Navegación actual

El prototipo utiliza `Shell`.

El flujo de entrada actualmente es:

```text
Inicio de aplicación
        ↓
QrScanPage
        ↓
FaceVerifyPage
        ↓
Creación de sesión mock
        ↓
ProfilePage
```

Una vez dentro del área del paciente existe navegación para:

```text
Inicio
Mi perfil
Historial clínico
Resultados
Documentos
Asistente IA
```

---

# Funcionalidades pendientes

Todavía NO están implementadas completamente:

- Integración real con Mobile BFF.
- Lectura real de códigos QR.
- Challenge QR real.
- Captura facial real.
- Verificación facial real.
- JWT real.
- Refresh token.
- Persistencia segura de sesión.
- Protección completa de rutas.
- Cierre de sesión completo.
- Expiración automática de sesión.
- Manejo completo de sesión expirada.
- Manejo avanzado de errores.
- Estados offline.
- Integración real con resultados clínicos.
- Integración real con documentos.
- Integración real con historial clínico.
- Integración real con SCI-PH AI.
- Pruebas automatizadas específicas del módulo Patient.
- Refactor completo a MVVM.
- `PatientRoutes`.
- `PatientServiceCollectionExtensions`.

Estas tareas deberán continuar después de integrar el módulo con la aplicación MAUI compartida.

---

# Arquitectura final esperada

El cliente móvil no debe conectarse directamente con:

- PostgreSQL.
- `IdentityDbContext`.
- `ClinicalDbContext`.
- Azure Blob Storage.
- Servicios internos de infraestructura.
- Proveedores biométricos directamente, salvo que la arquitectura lo determine explícitamente.

La comunicación del paciente debe realizarse mediante:

```text
.NET MAUI
    ↓
SCI-PH.Mobile.BFF
    ↓
Application / Infrastructure
    ↓
Servicios y base de datos
```

El endpoint técnico del Mobile BFF definido actualmente para desarrollo es:

```text
http://localhost:5191
```

---

# Integración con el proyecto MAUI compartido

Este prototipo utiliza actualmente el namespace:

```csharp
SCI_PH.PatientPrototype
```

Durante la integración con el proyecto MAUI compartido deberá adaptarse a:

```csharp
SCI_PH.Mobile
```

Por ejemplo:

```csharp
SCI_PH.PatientPrototype.Features.Patient.Auth
```

deberá convertirse en:

```csharp
SCI_PH.Mobile.Features.Patient.Auth
```

El objetivo final es tener una única aplicación:

```text
SCI-PH.Mobile/
│
├── Features/
│   │
│   ├── Paramedic/
│   │   └── Código del Rol 6
│   │
│   └── Patient/
│       └── Código del Rol 7
│
├── Extensions/
├── Resources/
├── App.xaml
├── AppShell.xaml
└── MauiProgram.cs
```

No se pretende mantener `SCI-PH.PatientPrototype` como una segunda aplicación móvil definitiva.

---

# Integración esperada de servicios

El proyecto MAUI compartido actualmente utiliza un patrón de registro de características.

El resultado esperado después de integrar los módulos es conceptualmente:

```csharp
builder.Services
    .AddCoreServices()
    .AddParamedicFeature()
    .AddPatientFeature();
```

El módulo Patient deberá implementar posteriormente:

```text
PatientServiceCollectionExtensions.cs
```

para registrar:

- Servicios del paciente.
- Servicios mock mientras sean necesarios.
- ViewModels.
- Views.
- Servicios de sesión.
- Clientes del Mobile BFF.

---

# Mobile BFF

SCI-PH utiliza un Backend for Frontend específico para la aplicación móvil:

```text
src/
└── Presentation/
    └── SCI-PH.Mobile.BFF
```

Los contratos previstos para el paciente incluyen, entre otros:

```http
POST /api/mobile/auth/qr/challenge
POST /api/mobile/auth/face/verify
GET /api/mobile/me
```

El prototipo NO implementa todavía llamadas HTTP reales a estos endpoints.

---

# Seguridad

Durante el desarrollo del prototipo se debe mantener:

- Ninguna contraseña en código.
- Ninguna cadena de conexión.
- Ninguna clave JWT.
- Ningún secreto.
- Ninguna API key real.
- Ningún dato biométrico persistente.
- Ninguna conexión directa con PostgreSQL.

Los valores utilizados actualmente corresponden únicamente a mocks/datos de demostración.

---

# Compilar

Desde:

```text
patient-prototype/SCI-PH.PatientPrototype
```

ejecutar:

```powershell
dotnet build
```

El proyecto ha sido probado compilando correctamente para:

```text
net9.0-android
net9.0-ios
net9.0-maccatalyst
net9.0-windows10.0.19041.0
```

---

# Ejecutar en Android

Con un dispositivo Android disponible:

```powershell
dotnet build -t:Run -f net9.0-android
```

Esto compila, instala y ejecuta la aplicación MAUI en Android.

---

# Flujo recomendado para probar el prototipo

```text
1. Abrir aplicación

2. Aparece pantalla QR

3. Continuar con autenticación simulada

4. Aparece verificación facial

5. Pulsar "Verificar identidad"

6. Se crea una sesión simulada

7. Se muestra confirmación de identidad

8. Se accede al área del paciente

9. Navegar por:
   - Inicio
   - Perfil
   - Historial
   - Resultados
   - Documentos
   - SCI-PH AI
```

---

# Datos Mock

Actualmente existen datos ficticios destinados únicamente al desarrollo de la interfaz.

Estos datos deberán reemplazarse gradualmente por contratos y respuestas del Mobile BFF.

No deben considerarse datos clínicos reales.

---

# Responsabilidad del Rol 7

La responsabilidad de este módulo es proporcionar la experiencia móvil del paciente relacionada con:

- Acceso mediante QR.
- Verificación facial 1:1.
- Sesión del paciente.
- Perfil.
- Historial autorizado.
- Resultados.
- Documentos autorizados.
- Asistente educativo SCI-PH AI.

---

# Responsabilidades externas

La integración completa depende también de otros roles.

## Rol 1

Responsable de coordinación arquitectónica e integración de la aplicación MAUI compartida.

## Rol 3

Autenticación, sesiones, permisos y documentos.

## Rol 8

Servicios relacionados con:

- QR.
- Verificación facial.
- IA.

## Rol 2

Información clínica disponible a través del backend.

## Rol 9

Configuración de entornos y endpoints.

## Rol 6

Infraestructura MAUI compartida y módulo Mobile / Paramedic.

---

# Nota para integración

Este directorio se entrega completo para permitir que el Rol 1:

1. Ejecute el prototipo del paciente de forma independiente.
2. Revise visualmente las pantallas.
3. Revise el flujo QR → rostro → sesión.
4. Identifique los componentes reutilizables.
5. Migre `Features/Patient` hacia `SCI-PH.Mobile`.
6. Combine la navegación con el módulo `Paramedic`.
7. Adapte los servicios al Mobile BFF.
8. Continúe el desarrollo junto al Rol 7.

No se debe reemplazar directamente el `AppShell`, `MauiProgram` o `.csproj` del proyecto MAUI compartido con los archivos de este prototipo.

Deben integrarse de manera controlada.

---

# Estado de entrega

Estado actual:

```text
Interfaz Patient             ✅
Home                         ✅
Perfil                       ✅
Historial                    ✅
Resultados                   ✅
Documentos                   ✅
Interfaz IA                  ✅
QR simulado                  ✅
Verificación facial simulada ✅
Sesión mock                  ✅
Navegación base              ✅
Compilación Android          ✅
Compilación Windows          ✅
Compilación iOS              ✅
Compilación MacCatalyst      ✅

Mobile BFF real              ⏳
QR real                      ⏳
Biometría real               ⏳
Sesión JWT real              ⏳
Protección de rutas          ⏳
Expiración / logout          ⏳
MVVM completo                ⏳
Pruebas automatizadas        ⏳
Integración SCI-PH.Mobile    ⏳
```

---

# Autor / módulo

Proyecto:

**SCI-PH**

Módulo:

**Mobile / Patient — Rol 7**

Rama de trabajo:

```text
feature/mobile-patient-app
```

Estado:

**Prototipo funcional pendiente de integración con SCI-PH.Mobile.**