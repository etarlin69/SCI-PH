# ADR-015: Autenticación del paciente e identificación por QR

- **Estado:** Aceptado. Decisiones 1, 2 y 6 implementadas en `feature/patient-self-service`. Decisiones 3, 4 y 5
  implementadas en `feature/patient-qr-identification` (QR #1); el escáner del paramédico, en `feature/paramedic-qr-scanner`
  (QR #2).
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
- Todo el expediente es **solo lectura**: el paciente no escribe datos clínicos con su cuenta. Su única escritura es su
  propia credencial QR, que no es un dato clínico (decisión 4).
- **Publicación conservadora en V1** (listas explícitas en `PatientSelfVisibility`, en contratos propios del paciente):
  diagnósticos solo `Definitive` de una atención `Closed`; resultados `Final` y `Corrected`, sin interpretación; recetas
  emitidas, sin las notas del prescriptor; alergias sin notas; atenciones sin motivo y con una clasificación propia
  (`InProgress`, `Completed`, `Cancelled`) en lugar del estado interno. Una atención cancelada aparece solo como tal: nada
  de lo registrado en ella se publica. Notas clínicas, signos vitales y procedimientos no se publican: no existe una
  clasificación de visibilidad para ellos.
- **Cada lectura se audita** (`ClinicalAuditEntry` `Read`, una por operación, solo metadatos) y **falla cerrado**: si la
  entrada no se puede guardar, no sale ningún dato (`503` genérico). No es un camino de atención de emergencia y la
  trazabilidad del acceso es obligatoria. Un rechazo dentro del servicio se audita como `Failure` si se puede.
- **Límite por usuario autenticado** (`patient-self-service`, 60 por minuto, sin cola), para frenar abusos; no es un límite
  clínico. Por eso el Mobile BFF ejecuta `UseAuthentication` antes de `UseRateLimiter`, como el Web BFF.
- **La app no guarda datos del paciente en el dispositivo:** sin caché clínica, perfil de solo lectura y
  `allowBackup="false"` en Android. Las pantallas se vacían al cerrar la sesión o cambiar de cuenta, y una respuesta tardía
  de otra cuenta se descarta.

### 3. El QR identifica; nunca autentica

- **El QR identifica al paciente clínico ante el personal autorizado. No autentica a nadie ni da acceso por sí solo.** Una
  copia, una foto o una captura tampoco conceden acceso al expediente: el acceso siempre depende de la autenticación, la
  autorización y las reglas de acceso del personal.
- Contenido: `sciphqr:v1:<token>`, donde `<token>` son **32 bytes de `RandomNumberGenerator` (256 bits)** en base64url sin
  relleno. El prefijo versiona el formato; una versión desconocida es un QR inválido, sin compatibilidad heredada.
- El QR **no** contiene `PatientId`, `IdentityUserId`, nombre, documento, fecha de nacimiento, email, datos médicos ni
  biométricos, rutas de Blob, JWT ni ningún token de autenticación.
- **Solo el hash en el servidor:** un `PatientIdentifier` de tipo `QrCode` y origen `System`, con
  `sha256:<hex en minúsculas>` de los bytes del token como valor. El token en claro no se guarda. La imagen se dibuja solo
  en la app: el servidor nunca guarda ni recibe PNG, SVG ni nada parecido.
- **Solo los emite el sistema.** Las API genéricas de pacientes del personal rechazan el tipo `QrCode` al registrar y al
  buscar (`400`), la resolución ignora cualquier valor que no sea de origen `System`, y un QR nunca es el identificador
  visible de un paciente, ni siquiera enmascarado.

### 4. Vida del QR

- **Vigencia de 15 minutos**, en el reloj del servidor (`ValidUntilUtc`). Sustituye a la decisión inicial de 365 días: limita
  lo que sirve una captura sin añadir estados ni tareas. Vencido, sigue `Active` hasta que otro lo sustituya; un QR vencido
  no resuelve.
- **Un QR activo por paciente.** Emitir revoca el vigente y crea otro en una transacción, con su auditoría. El índice único
  de identificadores primarios activos (`UX_PatientIdentifiers_ActivePrimary`) es la garantía final: de dos emisiones
  simultáneas, una confirma y la otra responde `409` sin haber emitido nada, y la app reintenta una vez.
- **Reemitir invalida el anterior.** El servidor no puede recuperar un token desde su hash: abrir "Mi QR" sin un token en
  memoria emite uno nuevo. Pedirlo en otro dispositivo invalida el del primero. No hay QR por dispositivo.
- **Para emitir, cuenta activa ahora.** El access token no basta (puede seguir válido minutos tras desactivar la cuenta,
  ADR-006): se comprueba `IIdentityService.FindActiveAccountAsync`, con `403` si no lo está. **Un QR ya emitido sigue siendo
  válido hasta vencer o revocarse aunque la cuenta se desactive después**, porque identifica al paciente clínico y no es
  autenticación; como mucho, durante 15 minutos. Revocar no exige cuenta activa.
- **Cerrar sesión no revoca el QR:** borra el token de la memoria y el QR vence solo en 15 minutos como máximo.
  `DELETE /api/mobile/patient/me/qr` lo revoca de forma explícita e idempotente.
- **En el dispositivo, solo en memoria:** nunca Preferences, SecureStorage, SQLite, archivos ni logs. Al cerrar la sesión,
  cambiar de cuenta o terminar el proceso desaparece.
- **Sin modo offline:** sin red y sin un token vigente en memoria, la app muestra el estado de red y nunca un QR vencido. El
  personal sigue teniendo huella, documento o NN; con el teléfono bloqueado o sin conexión, el QR no está disponible.
- **Capturas:** son posibles y no se bloquean en V1. Una captura solo identifica, caduca en 15 minutos como máximo y sigue
  exigiendo personal autenticado y autorizado, además de la confirmación humana.
- **Ciclo de vida:** `Patient` no tiene estado de fallecido, archivado ni fusión, así que la resolución no los comprueba.

### 5. Resolución por el personal

- El paciente **muestra** su QR; no escanea uno para iniciar sesión.
- **Resolver y confirmar** (`POST /api/mobile/emergencies/{id}/identify/qr/resolve` y `.../confirm`): el token va siempre en
  el cuerpo, nunca en la ruta ni en la query.
- **Personal:** autenticado, con `Emergencies.Operate`, `Patients.Read` y `Patients.Identify`, personal clínico activo y con
  acceso a esa emergencia, todo antes de mirar el QR. Sin atajo para el administrador: sin `ClinicalStaff` activo, `403`.
- **Confirmación humana obligatoria.** Resolver no cambia la emergencia: devuelve solo nombre, fecha de nacimiento y documento
  enmascarado, y como descubre un candidato sin identificar a nadie, queda como auditoría `Read` y **no** como
  `IdentificationAttempt` correcto. Confirmar vuelve a resolver el token en el servidor, sin sesión y sin confiar en el
  candidato anterior, registra **un** `IdentificationAttempt` correcto y usa `EncounterService.IdentifyPatientAsync`, la única
  vía de vincular un paciente: misma atención, código NN conservado, misma auditoría y mismo control de concurrencia. Repetir
  la misma confirmación no registra nada nuevo.
- **Límite aceptado en V1:** un QR revocado entre la última comprobación y el commit de `confirm` (milisegundos) aún puede
  identificar. No se añaden transacciones serializables ni bloqueos: el QR solo identifica, dura 15 minutos y exige personal
  autorizado y su confirmación.
- Un QR inválido por cualquier motivo (malformado, otra versión, desconocido, revocado o vencido) es el mismo `404`. Por
  dentro, `IdentificationAttempt` lo registra como `NoMatch` o `InvalidCredential`, sin token ni hash.
- **Auditoría:** emitir y revocar van en la misma transacción que el cambio; resolver revela datos personales y falla
  cerrado (`503` sin candidato). Nunca se audita ni se registra el token, su hash ni la imagen.
- **Límites por usuario, sin cola:** `patient-qr` 10/min (emitir y revocar) y `patient-qr-resolve` 30/min (resolver y confirmar).
- **QR #2:** escáner del paramédico con `ZXing.Net.Maui.Controls` **0.6.0** (la última versión comprobada para .NET 9; 0.7.4 y
  posteriores piden .NET 10), registrado con `UseBarcodeReader()` (generador y lector). Una página propia sobre la
  identificación; solo `QR_CODE`, una lectura por activación y solo el contenido `sciphqr:v1:` llega al servidor, tal cual.
  Leer solo muestra el candidato; identifica únicamente "Confirmar paciente", con un `clientRequestId` por candidato que se
  conserva en los reintentos. El permiso de cámara solo lo pide ese escáner (Android `CAMERA` con la cámara no obligatoria,
  iOS `NSCameraUsageDescription`); Windows usa la cámara de ZXing; en Mac Catalyst el escáner no se ofrece y no se declara la
  cámara. Detalle en [mobile/README.md](../../mobile/README.md#escáner-del-qr-del-paciente-paramédico).
- Fuera de alcance: verificación facial 1:1 **asistida** tras el QR. El rostro sigue siendo 1:1, nunca 1:N, autenticación
  ni autorización; si llegara a usarse como autenticación autónoma, la prueba de vida sería **obligatoria** (ADR-012).

### 6. Fuera de alcance de esta vertical

Descarga de documentos (el almacenamiento de documentos aún no existe), citas y notificaciones (no hay entidades), el
asistente de IA del paciente (no se conecta a la IA clínica) y la edición del perfil.

## Consecuencias

- La app tiene un solo inicio de sesión: la cuenta decide el área (paciente con rol `Patient`, paramédico con
  `Emergencies.Operate`). El paciente no recibe `Emergencies.Operate` ni ningún otro permiso para pasar el filtro.
- Un QR filtrado no expone el expediente; su riesgo es la identificación, y se mitiga revocando y reemitiendo.
- El QR es una vía más junto a huella, documento y NN: ninguna es requisito de otra, y en Mac Catalyst no se ofrece.
- Como para el personal, un access token emitido antes de desactivar la cuenta o revocar sus sesiones sigue siendo
  válido hasta 15 min (ADR-006, sin lista negra); el refresh se rechaza de inmediato.

## Seguimientos

- **P1 — `ForwardedHeaders`:** las políticas por IP (login, MFA, refresh) usan `RemoteIpAddress`; detrás de un proxy todos los
  clientes compartirían contador hasta configurarlo. No afecta al límite por usuario del autoservicio.
- **P1 — Corrección de identidad:** desvincular una cuenta mal vinculada, corregir un NN mal identificado y endurecer las
  invitaciones.
- **P1 — Recuperación de contraseña desde la app** y destino del paciente tras activar su cuenta.
- **P1 — Condiciones crónicas y medicación domiciliaria** en el expediente propio.
- **P1 — Paginación del historial del paciente:** hoy cada lista de `/api/mobile/patient/me` se corta en los 200
  elementos más recientes (orden determinista); falta una paginación por cursor.
- **P1 — Seguridad: ventana del access token tras desactivar o revocar** (heredada de ADR-006): hasta 15 min de uso de
  un token ya emitido. Endurecerla (security stamp o lista de revocación) es un cambio de ADR-006, no de esta vertical.
- **Integración HTTP real del área paramédica en MAUI:** `HttpParamedicApiService` todavía usa
  `MockParamedicApiService` internamente; es una vertical aparte (el backend paramédico no cambia aquí).
- **QR #2 — validación con cámara física:** el escáner está probado de extremo a extremo con la cámara virtual del emulador
  Android (evidencia provisional), y en Windows la webcam real abre y se libera, pero la lectura de un QR con una cámara física
  (webcam de Windows, y un Android físico antes de publicar) queda pendiente; ver mobile/README.md.
- **Endurecimiento común de la identificación:** si falla el guardado de una confirmación (documento, huella o QR), no se
  identifica nada, pero la respuesta no es un `503` controlado. Conviene resolverlo para las tres vías, no solo para el QR.
- **P1 — Web del hospital:** resolver un QR por escáner o pegándolo, con su propia ruta.
- **P1 — Revocar el QR al corregir la identidad:** la futura desvinculación o corrección debe revocarlo.
- **P1 — Botón para revocar el QR** en la app (el endpoint ya existe).
