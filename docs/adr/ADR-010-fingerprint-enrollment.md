# ADR-010: Registro (enrolamiento) de huellas

- **Estado:** Aceptado (implementado en `feature/biometric-fingerprint-enrollment`)
- **Fecha:** 2026-10-27
- **Relacionado:** [ADR-005](ADR-005-clinical-model.md) (decisión 9), [ADR-007](ADR-007-device-authentication.md),
  [ADR-009](ADR-009-fingerprint-identification.md), [contratos del Device API](../api/device-api-contracts.md),
  [contratos del Web BFF](../api/web-bff-contracts.md#web-hospital--huella-digital-implementado)

## Contexto

La identificación por huella en emergencias (ADR-009) usa `PatientBiometricReference` de un lector concreto, pero no había
forma de crearlas: solo una semilla de Development. El modelo sigue siendo A + C: la plantilla queda en el R307, que
compara localmente; SCI-PH solo guarda `Provider = "R307"` y `SubjectReference = r307:device:<DeviceCredentialId>:slot:<slot>`.
El índice único de referencias incluía las revocadas, así que un slot revocado no podía volver a usarse, y la
confirmación de una identificación leía la referencia sin protegerla de una revocación concurrente.

## Decisiones

### 1. Quién registra: `Biometrics.Manage`, y no basta solo

- Permiso nuevo `Biometrics.Manage` (no `Patients.Update`), asignado a Receptionist (y Administrator, que tiene todos).
- Además, el actor debe ser **personal clínico activo** (`ICurrentClinicalActor`) que **pueda ver al paciente** (la misma
  regla que leer el paciente). No hay atajo por nombre de rol: un Administrator sin `ClinicalStaff` recibe 403.
- `RegisteredByStaffId` sale siempre del actor que abrió el registro; nunca del cuerpo.

### 2. Dónde: el detalle del paciente en el Web del hospital

- `/hospital/pacientes/{id}` (nueva página de detalle, a la que lleva la búsqueda) con la tarjeta "Huella digital":
  referencias (lector, fecha, estado; nunca slot ni referencia interna) y "Registrar huella". No hay registro en MAUI.
- Antes de abrir el registro, una **confirmación operacional** ("Confirme que desea registrar la huella de este
  paciente"). **No es un registro formal ni legal de consentimiento:** no existe ese modelo y no se guarda ningún
  documento; solo se registra quién, cuándo, paciente, lector y resultado.

### 3. Qué lector: `Device.Fingerprint.Enroll` + `Device.Fingerprint.Identify`

- Scope nuevo `Device.Fingerprint.Enroll`, separado de `Identify`. El endpoint de registro del Device API exige Enroll;
  la identificación sigue exigiendo Identify.
- El Web solo ofrece lectores **activos con los dos scopes**: una referencia pertenece a su lector, y no tiene sentido
  registrarla en uno que no puede identificar.
- **Limitación del prototipo:** un paciente registrado en el lector A no lo está en el B. No hay sincronización ni
  exportación de plantillas.

### 4. El lector elige el slot; SCI-PH no asigna slots

- El dispositivo guarda la plantilla en un slot libre que elige él y lo informa. SCI-PH no asigna slots ni necesita
  listarlos. No se fija la capacidad ni la numeración del R307 (sin verificar): cualquier slot no negativo es válido.
- Al recibir `Enrolled(slot)` el backend valida: la sesión es de ese lector, está `Pending` y no venció; no hay otra
  referencia **activa** con esa `SubjectReference`; y el paciente no tiene otra referencia activa en ese lector.
- **Una referencia activa por paciente y lector** (regla de Application sobre el prefijo de `SubjectReference`; sin
  concepto de dedo).

### 5. Sesión de registro fuera del Domain

- `FingerprintEnrollmentSession`: estado técnico (abstracción en Application, modelo y store en Persistence), separado de
  la sesión de identificación. FK a paciente, personal y referencia creada; el lector es un valor escalar.
- Estados: `Pending` (el único activo: el backend no sabe cuándo el sensor captura), `Enrolled`, `Failed`, `Expired`,
  `Cancelled`. `LowQuality` y `SensorError` terminan en `Failed`: otro intento es otra sesión.
- `Status` es token de concurrencia; índices únicos filtrados: una `Pending` por paciente y una por lector.
- **120 s** de vida: *duración del prototipo; el tiempo real del R307 está pendiente de verificar.* El vencimiento se
  evalúa al usarla (consulta del lector, resultado, consulta y cancelación en el Web), sin proceso en segundo plano.
- El resultado se guarda como hash (`ResultHash`), no el slot: el slot de un registro exitoso está en la referencia.

### 6. Atomicidad y compensación: el dispositivo escribe primero

- Con `Enrolled(slot)`, en **una** transacción: `PatientBiometricReference.Register` (Active), la sesión `Enrolled` con su
  `BiometricReferenceId` y la auditoría. Si falla, no queda nada parcial.
- Reglas del dispositivo: **conserva el slot solo tras un 204**. Ante una respuesta incierta (sin respuesta, timeout,
  5xx) reintenta **exactamente el mismo resultado** (el replay da 204). Ante un rechazo terminal (404, 409: sesión
  vencida o cancelada, slot ocupado, otro resultado ya aceptado) o al vencer sin confirmación, **borra el slot** que
  acaba de escribir. No se confunde un error incierto con un rechazo.
- Un slot ocupado deja la sesión `Pending`: el dispositivo descarta ese slot y puede usar otro antes de que venza.

### 7. Referencias: unicidad solo entre activas y token de concurrencia

- El índice único `(Provider, Modality, SubjectReference)` pasa a ser **filtrado por `Status = Active`**
  (`UX_PatientBiometricReferences_ActiveSubject`): la referencia revocada queda como historia y su slot puede volver a
  registrarse. Todas las búsquedas de identificación siguen filtrando `Active`.
- `PatientBiometricReference.Status` es token de concurrencia. La confirmación de una identificación (ADR-009) carga la
  referencia y la **protege hasta el commit** (`EnsureBiometricReferenceUnchanged`, como `EnsureEncounterUnchanged`): una
  revocación confirmada antes hace perder la confirmación con 409 y la atención sigue NN.

### 8. Auditoría

- `ClinicalAuditEntry` del paciente (sin atención): `Create` de la sesión (personal), `Create` de la referencia (el
  lector, con la sesión, el lector y quién la pidió), `Update` de la sesión en un fallo o una cancelación (solo en la
  primera transición). Nunca slot, `SubjectReference`, plantilla, imagen ni clave. Sin `IdentificationAttempt`: está
  reservado a la identificación.

### 9. Lector revocado

- Sus referencias siguen `Active` pero son inutilizables: el lector no se autentica ni aparece como lector disponible.
  En el detalle del paciente se marcan como "Lector no disponible". Sin revocación masiva automática.

## Consecuencias

- Una huella registrada en el Web sirve de inmediato para identificar una emergencia con ese mismo lector (probado de
  punta a punta con el simulador).
- Fuera de alcance y para el siguiente PR: la revocación pública (invalidación inmediata en SCI-PH + trabajo de borrado
  del slot en el dispositivo + compensación), la reconciliación de slots huérfanos y el firmware real.
- Preguntas de hardware pendientes (documentación del R307): capacidad y numeración de slots; capturas y tiempo por
  registro; si informa slots ocupados; si detecta duplicados antes de guardar; si borra slots individuales; puntaje y
  umbral de la búsqueda; detección de dedos falsos; persistencia de los slots ante cortes de energía.
