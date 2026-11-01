# ADR-012: Verificación facial 1:1 asistida

- **Estado:** Aceptado. PR 1/2 implementado (proveedor Fake, #36). PR 2/2 (proveedor local OpenCV, plantillas cifradas y
  limpieza durable) implementado en `feature/biometric-face-opencv-provider`, con runtime nativo Linux propio construido
  desde source (ver [componentes de terceros](../legal/face-opencv-third-party.md)).
- **Fecha:** 2026-10-28
- **Relacionado:** [ADR-001](ADR-001-onion-architecture.md), [ADR-004](ADR-004-document-storage.md),
  [ADR-005](ADR-005-clinical-model.md) (decisión 9), [ADR-010](ADR-010-fingerprint-enrollment.md),
  [ADR-011](ADR-011-fingerprint-revocation.md),
  [contratos del Web BFF](../api/web-bff-contracts.md#web-hospital--rostro-implementado)

## Contexto

La verificación facial estaba prevista desde el inicio (README, `runtime-components.md`) pero solo existía como simulación
en la app del paciente (`FaceVerifyPage` + `MockPatientSessionService`), una interfaz `IFaceVerificationService` vacía y un
contrato previsto `POST /api/mobile/auth/face/verify`. Azure AI Face restringe Verify/Identify y el liveness (Limited
Access, solo clientes gestionados por Microsoft), así que no es viable para el proyecto.

## Decisiones

### 1. Solo 1:1, asistida, en el Web del hospital

- Caso de uso: el personal abre el **paciente X** (detalle del paciente), toma una foto con la cámara y SCI-PH la compara
  **solo** con la referencia facial activa de X → `Match` / `NoMatch`. Nunca una búsqueda entre pacientes (1:N): ningún
  endpoint recibe una cara sin paciente, y el servidor elige la referencia (el cliente no envía un id de referencia).
- **Sin prueba de vida (liveness) en esta fase.** Por eso es una herramienta **asistida**: el personal está presente y
  decide. **No** es una autenticación, ni una autorización, ni una prueba legal de identidad, ni un factor suficiente para
  abrir una cuenta. La UI lo dice ("Verificación asistida…").
- Una verificación **no identifica nada**: el paciente ya es el dato de entrada. No llama a `ClinicalEncounter.IdentifyPatient`,
  no cambia `PatientId` de ninguna atención y no crea `IdentificationAttempt` (ver 7).
- Fuera de esta fase: verificación del paramédico tras el documento o el QR, liveness. El login facial de la app del
  paciente (`POST /api/mobile/auth/face/verify`) quedó **sustituido** por
  [ADR-015](ADR-015-patient-authentication-qr-identification.md): el paciente inicia sesión con su cuenta y el rostro
  nunca emite una sesión; si alguna vez se usara como autenticación autónoma, el liveness sería obligatorio.

### 2. Proveedor detrás de `IFaceVerificationService`

- Application solo conoce el contrato: `Provider`, `AlgorithmVersion`, `EnrollAsync(capture)` → `Enrolled(subjectReference)`
  | `NoFace` | `MultipleFaces` | `LowQuality`; `VerifyAsync(subjectReference, capture)` → `Match` | `NoMatch` | `NoFace` |
  `MultipleFaces` | `LowQuality`; `DeleteAsync(subjectReference)` → `Deleted` | `NotFound`. Un fallo del proveedor
  (caído, timeout, error interno) es `ExternalServiceUnavailableException` → **503** genérico (mapeo global nuevo), nunca un
  resultado.
- **Nunca** cruza el contrato: puntaje de similitud, umbral, embedding, plantilla, modelo, SDK o dónde guarda el proveedor
  sus plantillas. El umbral es configuración del servidor, por proveedor y versión; el cliente nunca lo envía.
- El proveedor se elige en el servidor: `FaceVerification:Provider`. Sin configurar: `Fake` fuera de Production y
  `Disabled` (todo 503) en Production; `Fake` configurado en Production impide arrancar.

### 3. Normalización de la captura, independiente del proveedor

`IFaceCaptureNormalizer` (Infrastructure: `SkiaFaceCaptureNormalizer`, SkiaSharp, MIT) se aplica antes de cualquier
proveedor, así el PR 2 cambia el proveedor sin tocar la validación:

- `multipart/form-data` con exactamente `capture` y `clientRequestId` (cualquier otro campo —`patientId`, `provider`,
  `threshold`, `referenceId`, un segundo archivo— es 400). Sin base64.
- Máximo **2 MB**; JPEG o PNG por **magic bytes y decodificación real** (el Content-Type no cuenta; un archivo corrupto con
  el MIME correcto es 400); lado menor ≥ 480 px y ningún lado > 4096 px una vez orientada.
- Se aplica la orientación EXIF, se reduce a 1024 px en el lado mayor y se re-codifica como **PNG sin metadatos** (EXIF,
  GPS… desaparecen). El SHA-256 de la imagen normalizada solo reconoce un reintento.
- **Solo memoria:** el formulario no se vuelca a archivos temporales (`MemoryBufferThreshold`), los bytes crudos se borran
  tras decodificar; ni la imagen cruda ni la normalizada van a PostgreSQL, Blob, disco, logs, auditoría ni backups.

### 4. `PatientBiometricReference` sin cambios en el Domain

- `Modality = Face`, `Provider` (p. ej. `SCIPH-FaceFake`), `SubjectReference` **opaca** del proveedor (`face:<guid>`, sin
  paciente, nombre, email, documento ni fecha), `AlgorithmVersion` (p. ej. `fake-v1`), `RegisteredByStaffId`, estado y
  fechas. Nunca la plantilla.
- **Una referencia facial activa por paciente:** índice único filtrado `UX_PatientBiometricReferences_ActiveFacePerPatient`
  (`PatientId` donde `Modality = Face` y `Status = Active`; migración `AddActiveFaceReferencePerPatientIndex`). Las
  revocadas quedan como historia. Es la garantía final también con enrolamientos concurrentes.

### 5. Registro (Biometrics.Manage)

- `POST /api/web/patients/{patientId}/faces/enroll`: `Biometrics.Manage` + personal clínico activo + acceso al paciente
  (sin atajo por rol). Ya hay rostro activo → 409 **antes** de llamar al proveedor.
- Orden: autorizar → normalizar → recibo (`patient-face.enroll.v1`, hash del paciente y la imagen normalizada: el mismo
  pedido devuelve la misma referencia **sin volver a llamar al proveedor**; la misma clave con otra imagen → 409) →
  `EnrollAsync` → referencia + auditoría + recibo en una transacción (201). `NoFace`/`MultipleFaces`/`LowQuality`: 200 sin
  crear nada.
- **Compensación:** el proveedor registra antes del commit clínico. Si el commit falla (otro enrolamiento ganó en el índice
  único, error de base de datos…), se llama `DeleteAsync(subjectReference)` (mejor esfuerzo) y se conserva el error
  original. Con un proveedor real puede quedar una plantilla huérfana si la compensación también falla (ver 9).

### 6. Verificación (Patients.Identify)

- `POST /api/web/patients/{patientId}/faces/verify`: `Patients.Read` + `Patients.Identify` + personal clínico activo +
  acceso al paciente (no hace falta `Biometrics.Manage`).
- El servidor busca la referencia facial **activa** del paciente (de este proveedor); no hay → 409. `VerifyAsync`, y el
  resultado se registra con su recibo (`patient-face.verify.v1`) en una transacción; la referencia se protege con su token
  `Status`, así una revocación confirmada antes hace perder el registro (409).
- Todos los resultados biométricos son **200** con `outcome` (`Match`, `NoMatch`, `NoFace`, `MultipleFaces`,
  `LowQuality`); imagen inválida 400; sin permiso 403; paciente inexistente 404; sin rostro activo 409; proveedor 503.
- **Idempotencia:** el mismo `clientRequestId` con la misma imagen devuelve el resultado registrado **sin llamar al
  proveedor** (importante para proveedores que cobran por petición).

### 7. Registro de la verificación: `ClinicalAuditEntry`, no `IdentificationAttempt`

- `IdentificationAttempt` no exige atención (`ClinicalEncounterId` es opcional), pero representa **identificaciones**:
  su check `CK_IdentificationAttempts_Outcome` obliga a que un intento fallido **no** tenga paciente. Un `NoMatch` contra el
  paciente X perdería a X, y mezclaría verificaciones de un paciente conocido con identificaciones de desconocidos. No se
  deforma la entidad ni se crea una atención artificial.
- Cada verificación es una `ClinicalAuditEntry` `Read` de la entidad `FaceVerification` (id = el de la verificación) con
  `PatientId`, actor y `ChangesJson` `{ outcome, biometricReferenceId, provider, algorithmVersion }`. `Identify` queda
  reservado a la identificación de atenciones.

### 8. Revocación (Biometrics.Manage)

- `POST /api/web/patients/{patientId}/faces/{referenceId}/revoke`, motivo opcional cerrado (`PatientRequest`,
  `EnrollmentError`, `ReferenceOutdated`, `Other`; solo en la auditoría). La referencia debe ser del paciente y de rostro
  (si no, 404).
- `Active → Revoked`, su auditoría y **un** job `FaceTemplateDeletionJobs` `Pending` en **una** transacción
  (`PatientBiometricReference.Revoke`, sin cambios en el Domain); desde ese commit nunca se verifica contra ella. Ya
  revocada: 200 sin escribir nada.
- Después, un intento inmediato de borrar la plantilla; si falla, el job sigue `Pending` y lo reintenta el worker (§13).
  **La seguridad no depende del borrado:** la revocación lógica manda. Luego se puede registrar otro rostro.

### 9. Almacenamiento de plantillas (frontera para el PR 2)

- Se mantiene ADR-005 §9: el esquema `clinical` **nunca** guarda imagen facial, embedding, plantilla ni vector; solo la
  referencia opaca. La plantilla vive en un **almacén privado del proveedor**: Application no lo conoce (ni
  `IFaceTemplateStore` ni `IStorageService`; solo `IFaceVerificationService`).
- El Fake guarda su "plantilla" (un color) en memoria del proceso. El proveedor real guarda la suya cifrada (§12).

### 10. Proveedor Fake (PR 1)

`FakeFaceVerificationService`: determinista, sin red, claves, modelos ni hardware. Lee el color medio de capturas
**sintéticas generadas por programa** (nunca fotos de personas): negro → `NoFace`, rojo → `MultipleFaces`, gris medio →
`LowQuality`, azul puro → fallo del proveedor (503), cualquier otro color → una "cara" cuyo color coincide consigo mismo.
Sirve para CI, tests de integración, el E2E del PR 1 y desarrollo.

### 11. Auditoría y logs

| Evento | Acción / entidad | `ChangesJson` |
|---|---|---|
| Registro | `Create` `PatientBiometricReference` | `modality`, `provider`, `algorithmVersion` |
| Verificación | `Read` `FaceVerification` | `outcome`, `biometricReferenceId`, `provider`, `algorithmVersion` |
| Revocación | `Update` `PatientBiometricReference` | `status: [Active, Revoked]`, `reason` |
| Limpieza completada (actor `System` `face-template-cleanup`, una sola vez) | `Update` `PatientBiometricReference` | `cleanup: Deleted \| NotFound` |

Nunca en auditoría, logs ni respuestas: bytes de imagen, base64, embedding, plantilla, puntaje, umbral ni
`SubjectReference` (probado contra respuestas, auditoría y logs del Web BFF).

### 12. Proveedor local OpenCV (PR 2)

`FaceVerification:Provider=OpenCv` → `LocalOpenCvFaceVerificationService` (`Provider` `SCIPH-FaceOpenCV`,
`AlgorithmVersion` `opencv-sface-2021dec-yunet-2023mar`), en proceso, sin nube. Solo el Web BFF lo registra.

- **API administrada:** `OpenCvSharp5` 5.0.0.20261003, versión exacta; privada en Infrastructure y referenciada solo
  por el Web BFF (y los tests). Su analizador Roslyn (no carga con el SDK 9: CS9057) se quita solo en esos proyectos, por
  `NuGetPackageId`.
- **Runtime nativo de producción (Linux x64):** OpenCV 5.0.0 + OpenCvSharpExtern **construidos desde source** con commits
  fijados (`scripts/biometrics/build-opencv-linux.sh`, etapa `opencv-native` del Dockerfile): solo los módulos necesarios
  (`core, imgproc, imgcodecs, dnn, objdetect` y los contrib que OpenCvSharpExtern exige), librerías compartidas con
  `RPATH $ORIGIN`, sin FFmpeg, Tesseract, GStreamer, V4L, GTK/X11, videoio, highgui, text ni stitching. El script falla
  si alguno aparece en la configuración, en las dependencias ELF o embebido, y genera un manifiesto. El paquete oficial
  Linux de OpenCvSharp **no** se usa en producción.
- **Windows:** `OpenCvSharp5.runtime.win` (NuGet) solo para desarrollo y tests; no se distribuye (si se distribuyera un
  artefacto Windows, necesitaría su propio gate de licencias). Los tests en runners Linux usan el paquete oficial Linux
  como dependencia **solo de test**.
- **Modelos** (nunca en el repositorio): YuNet `face_detection_yunet_2023mar.onnx` (MIT) y SFace
  `face_recognition_sface_2021dec.onnx` FP32 (Apache-2.0), SHA-256 fijados en `FaceModels`. Se obtienen con
  `scripts/biometrics/fetch-face-models.{ps1,sh}` (URL fijada → temporal → SHA-256 → movimiento atómico) en `.models/face/`
  (ignorado). El Web BFF nunca descarga nada. YuNet 2026may dio resultados idénticos en el spike: se mantiene 2023mar.
- **Pipeline:** `ImDecode` → `SetInputSize` → YuNet → calidad → `AlignCrop` → `Feature` → normalización L2.
  Calidad (`FaceQuality`): ninguna cara ≥ `DetectorScore` 0.6 → `NoFace`; dos o más ≥ `GoodScore` 0.9 →
  `MultipleFaces`; la mejor < 0.9, lado menor < `MinFaceSide` 80 px o más del 10 % de su caja fuera de la imagen →
  `LowQuality`. Sin medida de desenfoque.
- **Decisión:** similitud coseno ≥ `CosineThreshold` 0.363 → `Match`. Valor derivado de upstream
  (`samples/dnn/face_detect.py`), **no calibrado para las webcams de SCI-PH**. Solo configuración del servidor: nunca en
  API, query, formulario ni header, y el puntaje nunca sale del proveedor.
- **Concurrencia:** pool acotado de pares detector+reconocedor (`EngineCapacity`, por defecto max(1, CPU/2) hasta 4) con
  `SemaphoreSlim`; el primero se crea al arrancar y todos se liberan al apagar. Buffers propios se limpian
  (`CryptographicOperations.ZeroMemory` / `Array.Clear`) y todo objeto de OpenCV se libera.
- **Plantilla:** `"SFT"` | formato 1 | longitud + `AlgorithmVersion` UTF-8 | dimensiones u16 | float32 LE (128). Lectura
  estricta: otro formato, algoritmo o dimensión → 503.
- **Sobre cifrado:** AES-256-GCM, `"SFE"` | versión 1 | longitud + keyId | nonce aleatorio (12) | tag (16) | texto
  cifrado. Datos asociados: la cabecera **y** el GUID de la plantilla (un cifrado copiado bajo otro id no descifra).
- **Claves:** `FaceVerification:OpenCv:Encryption:ActiveKeyId` y `Keys:<keyId>` (base64 de 32 bytes exactos). La activa
  cifra; todas las configuradas descifran (rotación sin re-cifrado masivo, que queda fuera de alcance). **Nunca** en
  appsettings versionados, base de datos, Blob, imagen Docker ni logs: User Secrets, variables de entorno o un almacén de
  secretos.
- **Almacén** (`IFaceTemplateStore`, interno a Infrastructure): Azure Blob (`Azure.Storage.Blobs` 12.29.2; Azurite en
  desarrollo), contenedor **privado** `face-templates`, un blob por plantilla con nombre `<guid>` y solo los metadatos
  `owner=sciph-face-opencv` y `format=1`; nunca sobrescribe (`If-None-Match: *`). `SubjectReference` = `face:<guid>`,
  parseado estrictamente.
- **Propiedad:** antes de verificar, la referencia debe ser de **este** proveedor y de **esta** `AlgorithmVersion`; si no
  (Fake, otro proveedor, otra versión) → 503 genérico y nunca se interpreta (hace falta volver a registrar). La limpieza
  tampoco envía a este proveedor referencias de otro.
- **Fallos:** almacén caído, plantilla ausente/corrupta/de otra clave o versión, motor con error → 503, **nunca**
  `NoMatch`. Registro: si falla el almacén no se escribe nada clínico; si falla el commit clínico se compensa con
  `DeleteAsync`; si también falla la compensación queda una huérfana que informa la reconciliación. Logs: solo categorías
  (`FaceTemplateMissing`, `FaceTemplateStoreUnavailable`, `FaceTemplateAuthenticationFailed`, `FaceReferenceInvalid`, …) y
  el tipo de excepción.
- **Arranque (fail fast)** con `Provider=OpenCv`: modelo ausente, hash distinto, `ActiveKeyId` ausente, clave que no es de
  32 bytes, cadena de conexión o contenedor inválidos → el host no arranca (mensajes sin claves ni rutas). El runtime
  nativo y el primer par se cargan al arrancar. Un almacén caído **no** impide arrancar: es 503 por petición. Sin health
  check en este PR (no tocar el PR #23): queda como seguimiento.
- **Disabled** sigue igual: arranca sin modelos ni claves, todo 503, sin worker. **Fake** sigue siendo el valor por defecto
  fuera de Production y está prohibido en Production; OpenCv sí se permite en Production.

### 13. Limpieza durable y reconciliación (PR 2)

- Tabla técnica `clinical.FaceTemplateDeletionJobs` (no es entidad del Domain): `Id`, `BiometricReferenceId` (FK única,
  `Restrict`), `Status` (`Pending`/`Completed`), `Outcome` (`Deleted`/`NotFound`), `Attempts`, `LastAttemptAtUtc`,
  `LastFailure` (`ProviderUnavailable`: se intentó y falló; `ProviderMismatch`: la referencia es de otro proveedor;
  `ProviderDisabled`: no hay proveedor configurado), `CreatedAtUtc`, `CompletedAtUtc`. `Status` y `Attempts` son tokens de
  concurrencia. Sin `SubjectReference`, paciente ni proveedor duplicados.
- **Proveedor Disabled:** la revocación lógica se aplica igual; el job queda `Pending` con `ProviderDisabled`, **sin**
  intento de borrado y **sin** incrementar `Attempts`; las pasadas lo omiten sin escribir nada (sin bucle activo). Cuando
  se configura un proveedor, se procesa como cualquier otro.
- `FaceTemplateCleanupService` (Application): intento inmediato tras la revocación y luego en cada pasada del
  `FaceTemplateCleanupWorker` (`BackgroundService` del Web BFF, `PeriodicTimer` de `Cleanup:IntervalSeconds` = 30, lotes de
  `BatchSize` = 50; solo con `Provider=OpenCv`). Reintentos tras 1, 2, 3, 5 y luego 10 minutos, sin límite; desde 5
  intentos fallidos el job **requiere atención** (derivado, no almacenado) y la reconciliación lo informa
  (`FaceTemplateCleanupAttention`, con los ids de los jobs).
- Varias instancias: cada una puede intentar el mismo job; solo un resultado se aplica (tokens) y converge en **un**
  `Completed` con **una** auditoría de finalización; un fallo de una pasada se registra y la siguiente reintenta.
- **Reconciliación** (`FaceTemplateReconciler`, cada `ReconcileIntervalMinutes` = 360, **solo informe**, nunca borra):
  A = referencia activa sin plantilla (`FaceTemplateMissing`); B = plantilla sin referencia retenida (`FaceTemplateOrphan`,
  con ids opacos); además, los jobs que requieren atención. Las huérfanas no se borran automáticamente.

### 14. Pruebas

- CI normal (Windows/Ubuntu): formato, sobre y claves (P0), calidad, servicio con analizador simulado, configuración fail
  fast, Disabled, almacén sobre Azurite (Testcontainers), reconciliación, limpieza durable (Application y PostgreSQL), y
  **carga del runtime nativo** sin modelos (`Cv2.GetVersionString`, API facial presente). El build de la imagen (job de
  contenedores) compila el runtime Linux propio y aplica su gate.
- Opt-in `SCIPH_FACE_MODELS=1` (tras `fetch-face-models` y `fetch-face-fixtures`): pipeline real sobre caras
  **sintéticas** de Face Synthetics (misma identidad transformada → `Match`, otra → `NoMatch`, `NoFace`, `MultipleFaces`,
  `LowQuality`, pool concurrente). Face Synthetics: Microsoft Research Use of Data Agreement (no comercial); nunca se
  versiona, ni va en Docker ni en artefactos de CI, y debe reemplazarse si SCI-PH se comercializa.

### 15. Despliegue

- Imagen del Web BFF: `dotnet publish -r linux-x64` (ningún asset nativo de Windows ni el runtime oficial de NuGet);
  etapa `opencv-native` (runtime propio, ver §12) que copia a `/app` solo `libOpenCvSharpExtern.so` y las 16
  `libopencv_*.so.500` que pide; etapa `face-models` que ejecuta el mismo script con verificación SHA-256
  (`--build-arg FACE_MODELS=include|none`, por defecto `include`) y copia a `/app/models/face`; avisos y manifiesto en
  `/app/licenses/`. Secretos (claves, cadena de conexión) solo en runtime.
- Configuración mínima (secretos fuera del repositorio):
  `FaceVerification__Provider=OpenCv`, `FaceVerification__OpenCv__Templates__ConnectionString=…`,
  `FaceVerification__OpenCv__Encryption__ActiveKeyId=k1`, `FaceVerification__OpenCv__Encryption__Keys__k1=<base64 32 bytes>`.

## Consecuencias

- Registro, verificación 1:1 y revocación de punta a punta con el proveedor Fake (PR 1) y con el proveedor local OpenCV
  (PR 2), con plantillas cifradas fuera del esquema clínico y limpieza durable.
- El runtime Linux propio se compila en el build de la imagen (más tiempo de build; la etapa queda en caché mientras no
  cambie el script). Sus componentes de terceros quedan identificados por versión y licencia.
- `Azure.Storage.Blobs` 12.29.x arrastra `Azure.Core` 1.55, que exige las abstracciones `Microsoft.Extensions.*` 10.0.3:
  Infrastructure las referencia en esa versión (el resto del host sigue en ASP.NET Core 9).
- SkiaSharp (MIT) entra en Infrastructure (con su librería nativa para Linux sin dependencias).
- Limitaciones aceptadas: sin liveness (una foto de una foto puede coincidir); umbral no calibrado para las webcams de
  SCI-PH; el Fake no reconoce caras.
- Seguimientos: estado de la limpieza durable en la tarjeta de rostro (P1; la seguridad ya depende de `Revoked`); health
  check del proveedor (tras el PR #23); salida 139 de un arranque fallido (preexistente en develop, aparte); validación
  manual con webcam (P2, demo).
