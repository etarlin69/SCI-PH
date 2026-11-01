# ADR-015: Autenticación del paciente e identificación por QR

- **Estado:** Aceptado. Decisiones 1, 2 y 6 implementadas en `feature/patient-self-service`; decisiones 3, 4 y 5 son el
  diseño del siguiente PR (QR), sin código todavía.
- **Fecha:** 2026-11-01
- **Relacionado:** [ADR-006](ADR-006-identity-security.md) (cuentas, MFA, refresh), [ADR-005](ADR-005-clinical-model.md)
  (`Patient.IdentityUserId`, `PatientIdentifier`), [ADR-012](ADR-012-face-verification.md) (rostro 1:1 asistido),
  [contratos del Mobile BFF](../api/mobile-bff-contracts.md#patient--cuenta-y-autoservicio)
- **Sustituye:** el flujo previsto `POST /api/mobile/auth/qr/challenge` → `POST /api/mobile/auth/face/verify` → sesión.

## Contexto

Los contratos del Mobile BFF preveían que el paciente entrara a la app escaneando un QR y pasando una verificación facial
1:1, y que esa verificación **emitiera la sesión**. La app lo simulaba (`QrAccessViewModel` → `FaceVerifyViewModel` →
`MockPatientSessionService`, con un paciente y un token ficticios). Ese diseño convertía el QR + el rostro en el factor de
autenticación del paciente, sin prueba de vida, y contradice ADR-012: el rostro es una verificación **asistida**, no una
autenticación ni un factor suficiente para abrir una cuenta.

Mientras tanto, el sistema de cuentas ya resuelve el problema: invitación por el personal, activación, contraseña, MFA
opcional, JWT, refresh rotado, logout y revocación de sesiones (ADR-006), y el vínculo cuenta → paciente mediante
`Patient.IdentityUserId` (único, escrito solo por `ClinicalIdentityLinkService` al activar la invitación).

## Decisiones

### 1. El paciente se autentica con su cuenta

- Flujo único: invitación del personal (`Accounts.InvitePatient`, al Email principal activo del paciente) → activación →
  inicio de sesión con contraseña en `/api/mobile/auth` → MFA si la cuenta lo tiene → JWT + refresh → logout. Los mismos
  endpoints, la misma audiencia (`sci-ph-mobile`) y la misma pila de la app que el personal clínico; cambian las reglas de
  autorización, no la autenticación.
- **Sin registro público**, sin creación anónima de cuentas y sin creación de cuentas por QR o por rostro.
- **El QR no autentica. El rostro no autentica. QR + rostro nunca emiten una sesión del paciente.** El flujo
  `qr/challenge` → `face/verify` → sesión queda **sustituido** y no se implementará.

### 2. El servidor deduce el paciente de la identidad autenticada

- Las rutas del paciente (`/api/mobile/patient/me/...`) **nunca** reciben un `patientId`: el servidor resuelve
  `UserId` → `Patient.IdentityUserId` → `Patient` y lee solo ese expediente.
- Exige una cuenta con el rol `Patient` **y** un vínculo clínico. Sin vínculo: `403` (falla cerrado). Nunca se vincula
  por email, nombre, documento ni teléfono, y no hay revinculación silenciosa.
- Un identificador de un recurso hijo (por ejemplo `encounterId`) solo puede aparecer en la ruta si el servidor vuelve a
  comprobar que pertenece al paciente; si no, `404` (no se revela que existe).
- El rol `Patient` sigue sin permisos globales. La lectura usa `IPatientAccessAuthorizer` (lectura propia, nunca
  escritura) y su propia ruta de aplicación (`PatientSelfService`): no finge ser `ClinicalStaff` y no relaja las reglas
  de personal, afiliación y clínica de los endpoints del personal. El personal tampoco entra en las rutas del paciente.
- Todo es **solo lectura**: el paciente no escribe datos clínicos con su cuenta.

### 3. Formato del QR (siguiente PR)

- Contenido: `sciphqr:v1:<token>`, donde `<token>` son **32 bytes aleatorios criptográficos (256 bits)** en base64url
  sin relleno. El prefijo versiona el formato.
- El QR **no** contiene `PatientId`, `IdentityUserId`, nombre, documento, fecha de nacimiento, email, diagnósticos ni
  datos médicos, rutas de Blob, JWT ni ningún token de autenticación.
- Persistencia: un `PatientIdentifier` de tipo `QrCode` (índice único de valores activos, revocación, `ValidUntilUtc`)
  que guarda el **SHA-256** del token, nunca el token en claro, si las restricciones del modelo lo permiten.

### 4. Vigencia del QR (siguiente PR)

- QR de larga duración, **revocable y reemitible**. Vigencia inicial por defecto de **365 días**, configurable en el
  servidor, mediante `ValidUntilUtc`. Sin QR rotativo de corta duración en esta fase.
- Motivo: el QR solo **identifica**. Un QR copiado o reenviado puede identificar al paciente, pero **nunca** autoriza el
  acceso al expediente: ese acceso exige una cuenta autenticada (paciente) o personal con permiso.

### 5. Escáner y rostro tras el QR (siguiente PR y fuera de alcance)

- La app del paciente **mostrará** su QR; no escanea un QR para iniciar sesión. El escáner es del paramédico, con
  `ZXing.Net.Maui.Controls` **0.6.0** (compatible con .NET 9; no se migra a .NET 10 por esto), y llega con el PR del QR.
- Fuera de alcance de esta vertical: paramédico autenticado → el QR identifica un candidato → verificación facial 1:1
  **asistida** opcional → decisión humana. El rostro sigue siendo 1:1, nunca 1:N, autenticación ni autorización.
- Si el rostro llegara a usarse como autenticación autónoma, la prueba de vida (liveness) sería **obligatoria** antes de
  aprobar ese diseño (ADR-012).

### 6. Fuera de alcance de esta vertical

Descarga de documentos (el almacenamiento de documentos aún no existe), citas y notificaciones (no hay entidades), el
asistente de IA del paciente (no se conecta a la IA clínica) y la edición del perfil.

## Consecuencias

- La app tiene un solo inicio de sesión: la cuenta decide el área (paciente con rol `Patient`, paramédico con
  `Emergencies.Operate`). El paciente no recibe `Emergencies.Operate` ni ningún otro permiso para pasar el filtro.
- Un QR filtrado no expone el expediente; su riesgo es la identificación, y se mitiga revocando y reemitiendo.
- Mientras no exista el PR del QR, el paciente no tiene QR en la app y el paramédico identifica por huella, documento o
  NN.
- Como para el personal, un access token emitido antes de desactivar la cuenta o revocar sus sesiones sigue siendo
  válido hasta 15 min (ADR-006, sin lista negra); el refresh se rechaza de inmediato.
