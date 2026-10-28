# ADR-011: Revocación de huellas y borrado del slot en el lector

- **Estado:** Aceptado (implementado en `feature/biometric-fingerprint-revocation`)
- **Fecha:** 2026-10-28
- **Relacionado:** [ADR-009](ADR-009-fingerprint-identification.md), [ADR-010](ADR-010-fingerprint-enrollment.md),
  [contratos del Device API](../api/device-api-contracts.md#borrado-del-slot-de-una-huella-revocada),
  [contratos del Web BFF](../api/web-bff-contracts.md#web-hospital--huella-digital-implementado)

## Contexto

ADR-010 creó las referencias de huella (`PatientBiometricReference`, `R307`, `r307:device:<DeviceCredentialId>:slot:<n>`)
pero no había forma pública de revocarlas, ni de borrar la plantilla que el R307 conserva en su slot. El índice único
filtrado por `Active` ya permite volver a usar una `SubjectReference` revocada, y `Status` ya es token de concurrencia.

**Regla fundamental: la revocación lógica en SCI-PH tiene prioridad.** Una huella revocada deja de servir para
identificar desde el commit de la revocación, aunque el lector esté apagado, sin red, revocado o perdido, o el borrado
físico falle. El borrado en el sensor es limpieza, no seguridad.

## Decisiones

### 1. Semántica: primero el estado lógico, en un solo commit con su trabajo de borrado

- `POST /api/web/patients/{patientId}/fingerprints/{referenceId}/revoke`, en **una** transacción
  (`IClinicalWriteStore.SaveChangesAsync`): la referencia pasa de `Active` a `Revoked`
  (`PatientBiometricReference.Revoke(now del servidor)`, sin cambios en el Domain), su `ClinicalAuditEntry` y **un**
  `FingerprintSlotDeletionJob` `Pending` con su propia entrada de auditoría. No puede existir una referencia revocada sin
  su trabajo, ni un trabajo con la referencia activa.
- Desde ese commit, todas las búsquedas de identificación (que filtran `Active`) dejan de encontrarla. La confirmación
  de una identificación ya `Matched` la vuelve a validar y la protege con el token `Status` (ADR-010 §7): si la
  revocación confirma antes, la confirmación recibe 409 y la atención sigue NN.
- **Nunca se borra físicamente primero.** Un borrado sin la revocación confirmada dejaría una referencia activa apuntando
  a un slot vacío, y un lector apagado bloquearía la seguridad.
- Respuesta `200` con el resumen de la referencia (`status`, `revokedAtUtc`, `cleanup`), leído de la base de datos: la
  misma respuesta para la primera revocación y para cualquier repetición.

### 2. Idempotencia y carreras

- Ya `Revoked`: `200` con el estado actual; nada escrito (ni auditoría, ni trabajo, ni nueva `RevokedAtUtc`).
- Dos revocaciones simultáneas: una gana en el token `Status`; la otra (sin escribir nada) vuelve a leer, la ve revocada
  y responde `200`. El índice único `UX_FingerprintSlotDeletionJobs_Reference` garantiza además **un trabajo por
  referencia**.
- **Sin trabajo de reparación en una repetición.** Una referencia revocada sin trabajo (historia anterior a esta
  decisión) se responde `200` con `cleanup: NotRequired` y no se crea nada: su slot pudo haberse reutilizado y un borrado
  tardío podría borrar la huella de otro paciente. La reconciliación de slots es otra funcionalidad.
- **Referencia heredada malformada:** si una referencia activa no tiene el formato del prototipo (proveedor distinto de
  `R307` o `SubjectReference` que `FingerprintReferences.TryParse` no acepta), se revoca igual (seguridad primero), con
  su auditoría, pero **sin** trabajo: no se envía a ningún lector una instrucción inválida. `cleanup: AttentionRequired`.

### 3. Quién: `Biometrics.Manage`, sin permiso nuevo

- El mismo actor que el registro (ADR-010 §1): `Biometrics.Manage` (Receptionist; Administrator por tener todos) **y**
  personal clínico activo que pueda ver al paciente. Sin atajo por rol: un Administrator sin `ClinicalStaff` recibe 403.
- La referencia debe ser del paciente de la ruta y de modalidad `Fingerprint`; otra, o ninguna: `404`.

### 4. Motivo: opcional, enum cerrado, solo en la auditoría

- `{ "reason": "PatientRequest" | "EnrollmentError" | "ReaderReplaced" | "Other" }`, opcional (sin cuerpo también vale).
  Sin texto libre (podría meter datos clínicos en la auditoría). Un valor desconocido o cualquier otro campo (`slot`,
  `subjectReference`, `deviceId`, `status`, `revokedAt`, `staffId`, `patientId`): `400`.
- El motivo vive **solo** en el `ChangesJson` de la auditoría; no se guarda en `PatientBiometricReference`.

### 5. Trabajo de borrado: tabla técnica propia

- No existía infraestructura de trabajos (ni `BackgroundService`, ni outbox). `FingerprintSlotDeletionJobs` en el esquema
  `clinical`, fuera del Domain (como las sesiones de ADR-009/010): `Id`, `BiometricReferenceId` (FK única, `Restrict`),
  `DeviceCredentialId` (escalar, sin FK al esquema identity), `Status`, `Outcome`, `Attempts`, `LastAttemptId`,
  `ResultHash`, `LastAttemptAtUtc`, `LastFailure` (categoría), `CreatedAtUtc`, `CompletedAtUtc`.
- **El slot no se guarda en el trabajo:** se deriva de la `SubjectReference` de la referencia (una sola fuente de
  verdad), con el único parser `FingerprintReferences.TryParse` (formato exacto, GUID en formato D, entero invariante y
  reconstrucción idéntica). No se guarda el paciente (se obtiene por la referencia), ni plantillas, ni imágenes.
- `Status` y `LastAttemptId` son tokens de concurrencia. Checks: ciclo de vida, intentos y fechas.

### 6. Estados mínimos y sin vencimiento

- Persistidos: `Pending` y `Completed` (con `Outcome` `Deleted` o `NotFound`; los dos significan que el slot ya no tiene
  la plantilla). `SensorError` **no** lo termina: sigue `Pending` para reintentarse. Sin `Failed` ni `Abandoned`.
- **Sin TTL:** la limpieza puede seguir siendo útil días después; la referencia ya es inútil.
- `cleanup` del Web es **derivado, nunca guardado**: `null` (activa), `Pending`, `Completed`, `AttentionRequired` (el
  trabajo sigue `Pending` y su lector está revocado, no existe o no tiene `Device.Fingerprint.Enroll`, o lleva ≥ 5
  intentos fallidos; o la referencia heredada no permite saber lector y slot) y `NotRequired` (revocada sin trabajo,
  historia). Llegar a 5 intentos **no** detiene los reintentos del lector.

### 7. Lector: `Device.Fingerprint.Enroll`, modelo pull

- Sin scope nuevo: la capacidad que escribe slots (y ya los borra como compensación en el registro) también los borra.
  Un lector solo `Identify` no consume trabajos (su limpieza aparece como `AttentionRequired`).
- `GET /api/device/fingerprint/deletions/pending`: el trabajo `Pending` más antiguo **de este lector** como
  `{ deletionId, slot }`. El slot es una instrucción técnica necesaria; nunca paciente, nombre, fecha de nacimiento,
  historia, `SubjectReference` ni `BiometricReferenceId`.
- `POST /api/device/fingerprint/deletions/{deletionId}/result` con `{ attemptId, outcome }`
  (`Deleted` | `NotFound` | `SensorError`). Sin `slot` (el servidor lo conoce) ni `deviceId` (el lector es la credencial):
  `400`. Trabajo de otro lector o inexistente: `404` (no se distinguen).

### 8. `attemptId`: un intento físico se cuenta una sola vez

- El lector genera un `attemptId` por cada intento **físico** y lo conserva al reenviar ese resultado (respuesta
  perdida). Sin él, un 204 perdido de un `SensorError` sumaría un intento falso y podría activar `AttentionRequired`.
- Reglas (se recuerda el último intento: `LastAttemptId` + `ResultHash` = SHA-256 de intento y resultado):
  - mismo intento y mismo resultado: `204`, nada escrito; mismo intento con otro resultado: `409`;
  - intento nuevo con el trabajo `Pending`: `SensorError` suma un intento (`Attempts`, `LastAttemptAtUtc`, `LastFailure`)
    y sigue `Pending`; `Deleted` o `NotFound` lo completan (`Outcome`, `CompletedAtUtc`);
  - intento nuevo con el trabajo `Completed`: `409` (su resultado no cambia nunca).
- Dos informes simultáneos: uno se aplica y el otro se decide de nuevo sobre lo que dejó (el mismo intento es una
  repetición; un trabajo completado lo rechaza).

### 9. Protección contra la reutilización del slot pendiente de borrar (P0)

- Mientras el trabajo está `Pending`, la plantilla revocada sigue en el slot. Si otro paciente se registrara en ese slot
  (estado del simulador reiniciado, error de firmware, lista de slots desactualizada), el trabajo pendiente borraría
  **su** plantilla. Por eso `FingerprintEnrollmentReaderService` rechaza con `409` un `Enrolled(slot)` cuyo
  `r307:device:<lector>:slot:<slot>` tiene un trabajo `Pending` de ese lector (el registro sigue `Pending` y el lector
  compensa su escritura). Al completarse el borrado, el slot vuelve a poder registrarse.
- Orden de las comprobaciones: primero la referencia activa, después el trabajo pendiente. Como la revocación confirma el
  estado `Revoked` y su trabajo juntos, si la primera consulta ya no ve la referencia activa la segunda (posterior) ya ve
  su trabajo.
- No se bloquea el registro en todo el lector: solo esos slots. El simulador elige el primer slot libre según su propio
  archivo y normalmente no los elegiría.

### 10. Identificación mientras la limpieza está pendiente

- El sensor puede seguir devolviendo `Match(slot)`: el backend no encuentra referencia **activa** y resuelve `NoMatch`
  con un `IdentificationAttempt` fallido (`NoMatch`), sin candidato, con el mismo `204` genérico que un slot desconocido.
  No revela que la huella estuviera revocada.

### 11. Auditoría

| Evento | Acción / entidad | Actor | `ChangesJson` |
|---|---|---|---|
| Referencia revocada | `Update` `PatientBiometricReference` | personal | `status: [Active, Revoked]`, `reason` |
| Borrado solicitado | `Create` `FingerprintSlotDeletionJob` | personal | `biometricReferenceId`, `readerId` |
| Borrado completado | `Update` `FingerprintSlotDeletionJob` | lector | `status: [Pending, Completed]`, `outcome` |
| Primer error del sensor | `Update` `FingerprintSlotDeletionJob` | lector | `failure: SensorError` |

- Con el `PatientId` del paciente y sin atención. Solo el **primer** `SensorError` de cada trabajo se audita; los
  siguientes quedan en `Attempts`, `LastAttemptAtUtc` y `LastFailure` (sin inundar la auditoría).
- Nunca slot, `SubjectReference`, clave, hash de credencial, plantilla ni imagen. El slot existe solo como dato técnico
  de persistencia (dentro de `SubjectReference`) y en la respuesta del Device API al lector dueño. Sin
  `IdentificationAttempt` para la limpieza.

### 12. Lector revocado o perdido

- Un `DeviceCredential` revocado no se autentica (no hay reactivación) y no puede borrarse (solo revocarse); trabajos y
  referencias lo nombran con un valor escalar, sin cascadas sobre la historia biométrica.
- Su trabajo queda `Pending` (sin `Abandoned` en esta versión) y el Web muestra `AttentionRequired` ("Lector no
  disponible. La huella ya está desactivada en SCI-PH; la limpieza física requiere atención."). No se revocan
  automáticamente las demás referencias del lector.

### 13. Web del hospital

- En la tarjeta "Huella digital", una referencia `Active` tiene "Revocar huella" → diálogo de confirmación ("La huella
  dejará de servir para identificar de inmediato. El lector borrará su copia cuando se conecte.") con motivo opcional
  (lista cerrada). Tras el 200 la referencia se muestra `Revocada` al instante, con "Huella desactivada en SCI-PH" y la
  limpieza aparte: "Limpieza del lector pendiente", "Lector limpio", "Requiere atención" o "Sin limpieza automática
  pendiente". La UI nunca sugiere que siga siendo válida mientras se limpia.
- Sin botón de reintento: el lector reintenta solo. Nunca se muestran slot, `SubjectReference`, intentos ni fallos.

## Consecuencias

- Ciclo de vida completo en el prototipo: registro → identificación → revocación inmediata → borrado físico con
  reintentos → slot reutilizable. Probado de punta a punta con el simulador (modo `enroll`), sin hardware real.
- Migración `AddFingerprintSlotDeletionJobs` (esquema `clinical`); sin cambios en el Domain ni en el esquema identity.
- **Seguimiento (seguridad biométrica, P1/P2):** una sesión de identificación ya `Matched` puede confirmarse aunque su
  lector se revoque entre el `Match` y la confirmación (la confirmación valida la referencia, no el lector). No se
  resuelve aquí.
- Fuera de alcance: reconciliación general de slots, estado `Abandoned`, borrado manual o reintento desde el Web,
  revocación masiva al revocar un lector, firmware real.
- **Preguntas de hardware pendientes (R307):** comando de borrado por slot; si el borrado es síncrono; qué devuelve con
  un slot vacío (¿`NotFound` o éxito?); códigos de error; persistencia del borrado ante cortes de energía; si permite
  enumerar los slots ocupados (necesario para la reconciliación).
