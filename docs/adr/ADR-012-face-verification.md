# ADR-012: Verificación facial 1:1 asistida

- **Estado:** Aceptado. PR 1/2 implementado en `feature/biometric-face-verification` (proveedor Fake); PR 2/2 (proveedor
  local OpenCV) planificado.
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
- Fuera de esta fase: login facial de la app del paciente (sigue **simulado**, `POST /api/mobile/auth/face/verify`
  **previsto**; requerirá liveness del proveedor), verificación del paramédico tras el documento, liveness.

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
- `Active → Revoked` con su auditoría en una transacción (`PatientBiometricReference.Revoke`, sin cambios en el Domain);
  desde ese commit nunca se verifica contra ella. Ya revocada: 200 sin escribir nada.
- Después, `DeleteAsync` de la plantilla (mejor esfuerzo) y una entrada `{ cleanup: Deleted | NotFound | Failed |
  NotAttempted }`. **La seguridad no depende del borrado:** la revocación lógica manda. Luego se puede registrar otro rostro.

### 9. Almacenamiento de plantillas (frontera para el PR 2)

- Se mantiene ADR-005 §9: el esquema `clinical` **nunca** guarda imagen facial, embedding, plantilla ni vector; solo la
  referencia opaca. La plantilla vive en un **almacén privado del proveedor**: Application no lo conoce (ni
  `IFaceTemplateStore` ni `IStorageService`; solo `IFaceVerificationService`).
- El Fake guarda su "plantilla" (un color) en memoria del proceso. El proveedor real guardará la suya cifrada (ver el plan).

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
| Limpieza | `Update` `PatientBiometricReference` | `cleanup` |

Nunca en auditoría, logs ni respuestas: bytes de imagen, base64, embedding, plantilla, puntaje, umbral ni
`SubjectReference` (probado contra respuestas, auditoría y logs del Web BFF).

## Plan del PR 2/2 (no implementado)

- `LocalOpenCvFaceVerificationService`: detector **YuNet** (licencia **MIT**) + reconocedor **SFace** (licencia
  **Apache-2.0**), vía OpenCV en Infrastructure. Sin cambios en Application ni en la normalización.
- Plantillas en un **contenedor Blob privado propiedad del proveedor** (`IFaceTemplateStore` interno a Infrastructure),
  **cifradas antes de guardarse**, con nombre de objeto **aleatorio y opaco** (nunca el `PatientId`). Nunca una tabla
  clínica.
- Imagen de registro y de verificación: nunca guardadas. Umbral: configuración versionada del servidor.
- Archivos de modelo: **no** se suben al repositorio; script de descarga con verificación de checksum. Verificar las
  dependencias nativas en Windows y Linux (contenedores, CI).
- **Bloqueante para el PR 2:** una estrategia **durable** de limpieza/reconciliación de plantillas (borrado tras la
  revocación que sobreviva a fallos y reconciliación de huérfanas) antes de considerarlo completo para producción.

## Consecuencias

- Registro, verificación 1:1 y revocación de punta a punta con el proveedor Fake, sin datos biométricos reales.
- SkiaSharp (MIT) entra en Infrastructure (con su librería nativa para Linux sin dependencias).
- Limitaciones aceptadas: sin liveness (una foto de una foto puede coincidir); el Fake no reconoce caras; el login facial
  del paciente sigue simulado.
