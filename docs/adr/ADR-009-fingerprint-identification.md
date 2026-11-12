# ADR-009: Identificación por huella en la emergencia prehospitalaria

- **Estado:** Aceptado (implementado en `feature/biometric-fingerprint-identification`)
- **Fecha:** 2026-10-27
- **Relacionado:** [ADR-005](ADR-005-clinical-model.md) (decisión 9), [ADR-007](ADR-007-device-authentication.md),
  [ADR-008](ADR-008-prehospital-emergency.md), [contratos del Device API](../api/device-api-contracts.md),
  [contratos del Mobile BFF](../api/mobile-bff-contracts.md#identificación-por-huella--implementado)

## Contexto

El paramédico necesita identificar al paciente NN de una emergencia por su huella cuando no puede identificarse por sí
mismo. El hardware es un ESP32 con un sensor R307. El modelo ya existía en el Domain sin uso: `PatientBiometricReference`
(solo una referencia del proveedor; nunca imágenes ni plantillas, ADR-005) e `IdentificationAttempt` (cada intento y su
resultado). La autenticación de dispositivos (ADR-007) ya era real, con el scope `Device.Fingerprint.Identify` sin
ningún endpoint. El contrato previsto del Device API enviaba la plantilla al servidor, recibía el `deviceId` en el cuerpo
y devolvía datos del paciente al dispositivo, y la app móvil simulaba la lectura y marcaba al paciente como identificado
sin que el servidor lo supiera.

## Decisiones

### 1. La plantilla no sale del sensor; la comparación es del sensor (A + C)

- SCI-PH **no guarda imágenes ni plantillas y no compara huellas**. El R307 guarda la plantilla en un slot y compara
  localmente; el lector solo informa qué slot coincidió.
- SCI-PH guarda únicamente `PatientBiometricReference` con `Provider = "R307"`, `Modality = Fingerprint` y
  `SubjectReference = r307:device:<DeviceCredentialId>:slot:<slot>`. Se usa el id de la credencial del dispositivo (estable),
  no su código (una etiqueta humana que podría cambiar). La referencia no se envía a la app ni al lector, ni se escribe
  en logs ni en auditoría.
- El rango real de slots del R307 no se fija en el código (no se verificó su documentación): cualquier slot no negativo
  tiene un formato válido.
- **Limitación aceptada del prototipo:** una referencia pertenece a un lector concreto. Un paciente enrolado en el
  lector A **no** está enrolado en el lector B. No hay sincronización de plantillas.

### 2. Una sesión por captura, fuera del Domain

- `FingerprintIdentificationSession` es **estado técnico** (abstracción `IFingerprintSessionStore` en Application, modelo
  y store en Persistence), no una entidad del Domain, como los recibos de idempotencia. Vive en el esquema clínico para
  confirmarse en la misma transacción que el intento, la identificación, la auditoría y el recibo. Migración
  `AddFingerprintIdentificationSessions`.
- Liga **una** captura a **una** emergencia (FK), **un** lector (`DeviceCredentialId` como valor: el lector está en el
  esquema de identidad y no hay FK entre contextos) y **un** paramédico (`RequestedByStaffId`).
- Estados: `Pending` (espera al lector), `Matched` (espera la confirmación), `NoMatch`, `Failed`, `Expired`, `Cancelled`,
  `Confirmed`. `Status` es token de concurrencia. Índices únicos filtrados: una sesión activa (`Pending`/`Matched`) por
  emergencia y una `Pending` por lector. Restricciones CHECK sobre el ciclo de vida.
- El resultado del lector se guarda como hash SHA-256 (`ResultHash`), nunca el slot en claro: basta para reconocer un
  reintento idéntico.

### 3. Correlación sin nonce

- La correlación y el antirreplay son: `sessionId` aleatorio, lector autenticado (ADR-007), sesión ligada a su
  `DeviceCredentialId`, expiración corta, HTTPS, estado terminal y el token de concurrencia.
- **No hay nonce separado:** guardado solo como hash no podría volver a entregarse si se pierde la respuesta del GET,
  y guardarlo recuperable añade un secreto sin beneficio en este prototipo. El lector solo conoce sus propias sesiones.

### 4. 90 segundos y modelo pull

- La sesión vence a los **90 segundos** de abrirse, tanto esperando al lector como esperando la confirmación.
- El ESP32 **consulta** si tiene una sesión pendiente (`GET /api/device/fingerprint/sessions/pending`). Sin push,
  WebSocket ni MQTT.
- No hay proceso en segundo plano: el vencimiento se evalúa al usar la sesión (abrir otra, consultar, recibir un
  resultado, confirmar, cancelar), que la cierra como `Expired`. Una sesión vencida se comporta siempre como `Expired`.

### 5. Confirmación humana obligatoria

- Una coincidencia **no** identifica la emergencia. El flujo es: el sensor coincide → sesión `Matched` → la app muestra
  el candidato mínimo (nombre y fecha de nacimiento, sin id) → el paramédico confirma → el servidor identifica.
- La confirmación no recibe paciente: lo toma de la referencia de la sesión, que debe seguir activa. Usa el caso de uso
  real (`EncounterService.IdentifyPatientAsync`): `IdentifiedByStaffId` es siempre el paramédico autenticado, se audita
  `Identify` y `PatientId` es token de concurrencia.
- El dispositivo nunca llama a `ClinicalEncounter.IdentifyPatient` ni tiene permisos clínicos.

### 6. Selección del lector

- El paramédico elige el lector en cada sesión entre los activos con `Device.Fingerprint.Identify`
  (`GET /api/mobile/devices/fingerprint-readers`).
- **Limitación aceptada:** no hay relación lector–ambulancia, lector–clínica ni lector–base; cualquier lector activo
  con el scope aparece para cualquier paramédico.

### 7. Ningún dato clínico al dispositivo

- El lector recibe solo `sessionId` y `expiresAtUtc`; un resultado se responde con `204` o un error genérico. Nunca
  recibe paciente, `PatientId`, nombre, fecha de nacimiento, datos de la emergencia, registros, documentos ni historia,
  ni la referencia o el slot esperado.
- Errores al lector genéricos: no distingue una sesión ajena de una inexistente (`404`) ni un slot conocido de uno
  desconocido (ambos se aceptan y uno queda sin candidato).

### 8. Intentos y auditoría

- Cada resultado real del lector crea **exactamente un** `IdentificationAttempt` (`Fingerprint`, `DeviceReference` = código
  del lector, `ProviderRequestReference` = `sessionId`, `PerformedByStaffId` = el paramédico de la sesión, la emergencia),
  salvo un reintento idéntico. `Match` con referencia activa: éxito del cotejo (todavía no identifica). Sin candidato:
  `NoMatch` (también un slot sin referencia activa de ese lector), `LowQualityCapture`, `ProviderUnavailable`.
- `ClinicalAuditEntry`: `Create` al abrir la sesión, `Update` al cancelarla (solo en la primera transición), `Identify`
  al confirmar. Nunca plantilla, imagen, clave, slot ni `SubjectReference` en `ChangesJson`.

### 9. La identificación móvil termina en la entrega

- La identificación desde el móvil, **por documento y por huella**, exige `ClinicId == null` (la regla de escritura
  prehospitalaria de ADR-008, decisión 11): después de que el hospital recibe la emergencia, `409` para el paramédico que
  participó aunque esté afiliado a la clínica destino. La identificación hospitalaria por el Web no cambia.

### 10. Referencias sembradas hasta el enrolamiento

- Hasta el enrolamiento, las referencias se creaban solo en Development (opción
  `ClinicalDevelopmentSeed:FingerprintReferences`) y en los tests; la opción sigue disponible solo en Development. Nunca
  una semilla biométrica de producción ni una clave de dispositivo en el código. El enrolamiento real está en
  [ADR-010](ADR-010-fingerprint-enrollment.md), que además protege la referencia en la confirmación (token de
  concurrencia: una revocación concurrente hace perder la confirmación con 409) y limita la unicidad de
  `SubjectReference` a las referencias activas.

### 11. El lector de la sesión se revalida en la confirmación y al mostrar el candidato

*(Añadida en `feature/biometric-device-revocation-hardening`, 2026-10-28.)*

- **Problema:** el lector pudo ser válido al informar el `Match` y dejar de serlo después (revocado por la
  administración, inexistente o sin `Device.Fingerprint.Identify`). Antes, una sesión `Matched` se confirmaba igual
  durante sus 90 s.
- **Regla:** la confirmación solo se completa si la emergencia sigue permitida (NN, sin clínica, acceso del paramédico),
  la sesión es suya, está `Matched` y no vencida, la referencia sigue activa **y el lector de la sesión sigue siendo un
  lector de huella válido**: `DeviceCredential` `Active` con `Device.Fingerprint.Identify` (la misma regla, `IsReader`,
  que al listar lectores y abrir sesiones). Se consulta con `IDeviceCredentialStore.FindAsync`, sin SQL directo, sin
  consultas de `ClinicalDbContext` al esquema identity y sin cambios en el Domain ni migración.
- **Si no lo es:** `409` con un mensaje genérico (no distingue revocado, inexistente o sin scope); nada escrito: la
  atención sigue NN, sin `PatientId`, sin auditoría `Identify` y sin recibo. La sesión **sigue `Matched`** (no se escribe
  un estado clínico solo para representar el fallo de un dispositivo): el paramédico la cancela o vence sola. La
  referencia biométrica no se toca.
- **Idempotencia:** un reintento con la misma clave de una confirmación rechazada se evalúa de nuevo (otra vez `409`). Una
  confirmación ya committed antes de la revocación es una identificación válida y su reintento sigue devolviendo la
  emergencia identificada: nunca se convierte en un error retroactivo.
- **El candidato es una vista de seguridad:** `GET` de la sesión reevalúa el lector en cada lectura. Si ya no es válido,
  la sesión puede seguir `Matched` pero `candidate` es `null`, sin decir por qué (como con una referencia revocada).
- **Orden y garantía entre contextos:** el lector vive en el esquema identity (`IdentityDbContext`, otra conexión;
  [ADR-003](ADR-003-postgresql-two-contexts.md)) y no hay transacción común con la confirmación clínica. La comprobación
  del lector es el **último** paso, justo antes de `EncounterService.IdentifyPatientAsync`, y es el punto de
  linealización. **SCI-PH garantiza** que una confirmación se rechaza si la revocación del lector ya estaba committed
  cuando se ejecuta esa comprobación final. **No garantiza** que una revocación que se confirma después de esa
  comprobación (en los milisegundos hasta el commit clínico) gane sobre la confirmación: ese caso equivale a "la
  confirmación terminó antes que la revocación". Es un límite aceptado: no se usan transacciones distribuidas, bloqueos
  entre esquemas (`FOR SHARE` desde Clinical), outbox ni sellos de versión del lector. Revocar un lector es una medida
  reactiva y una confirmación siempre es una decisión humana sobre un candidato.
- **App (MAUI):** ante el `409` de la confirmación muestra el mensaje seguro, vuelve a consultar la sesión y, si la
  encuentra `Matched` sin candidato, la cancela en el servidor en silencio (como ya hacía la consulta periódica). Así se
  libera la sesión activa de la emergencia: se puede abrir otra lectura de inmediato, y documento y NN siguen
  disponibles. Nunca muestra "lector revocado".
- Sin otros efectos de la revocación de un lector: sus sesiones `Pending` y registros pendientes vencen solos (el lector
  ya no se autentica), sus trabajos de borrado quedan `Pending` con limpieza `AttentionRequired`
  ([ADR-011](ADR-011-fingerprint-revocation.md)) y sus referencias activas no cambian (ninguna sesión nueva puede usar
  un lector revocado).

## Consecuencias

- La identificación por huella es real de punta a punta con el simulador (`tools/SCI-PH.Device.Simulator`, modo
  `fingerprint`); el firmware real del ESP32/R307 es otro trabajo (Rol 9).
- Quedan fuera: el enrolamiento (ahora en ADR-010; el borrado del slot en el sensor sigue pendiente), la verificación facial,
  la relación lector–ambulancia, la sincronización de plantillas entre lectores y la detección de dedos falsos (depende
  del hardware).
- Una sesión `Matched` cuya referencia se revoca deja de tener candidato y no se puede confirmar; la app la cancela para
  poder leer de nuevo.
- Sin conexión, la sesión vence sola; documento y NN nunca se bloquean.
