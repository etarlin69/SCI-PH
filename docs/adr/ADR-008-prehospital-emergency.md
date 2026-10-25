# ADR-008: Emergencia prehospitalaria: acceso sin clínica e idempotencia

- **Estado:** Propuesto para revisión en el PR de `feature/mobile-emergency-foundation`
- **Fecha:** 2026-10-23
- **Relacionado:** [ADR-005](ADR-005-clinical-model.md), [ADR-006](ADR-006-identity-security.md),
  [contratos del Mobile BFF](../api/mobile-bff-contracts.md#paramedic-workflow)

## Contexto

El Domain ya admite una atención `Prehospital` sin clínica y con paciente NN (ADR-005), y la recepción de un traslado
le asigna la clínica destino a la misma atención (`ReceiveAtClinic`). Sin embargo, todos los casos de uso de Application
exigían una clínica del usuario (`ClinicalActor.EnsureClinic`), así que ningún canal podía abrir ni leer una atención
sin clínica. Además, la app móvil reintenta la apertura cuando se pierde la respuesta y el servidor no tenía forma de
reconocer el reintento: cada reintento creaba otra atención NN.

## Decisiones

### 1. La atención prehospitalaria no tiene clínica hasta llegar

- El paramédico abre la emergencia como `ClinicalEncounter.CreateUnidentified(Prehospital, startedAt, clinicId: null)`.
- La **clínica base del paramédico no representa el lugar de la emergencia**: no se usa como `ClinicId`, no aparece
  en las listas de atenciones abiertas de esa clínica y no da acceso a la emergencia.
- Al llegar, la recepción del traslado (`ReceiveAtClinic`) asigna la clínica destino a la **misma** atención: mismo
  id, mismo código NN y mismos registros.

### 2. El acceso al caso concreto es la asignación

- `Emergencies.Operate` da acceso al módulo; cada acción exige además su permiso granular (`Encounters.Create`,
  `Encounters.Read`…). Nunca se comprueba un rol.
- El usuario debe seguir siendo un `ClinicalStaff` activo con al menos una afiliación activa (`ICurrentClinicalActor`).
- `IEncounterAccessService` decide el acceso a una atención concreta:
  - **con clínica**: la regla existente (`EnsureClinic`), sin cambios;
  - **`Prehospital` sin clínica**: solo con una `EncounterStaffAssignment` activa del `ClinicalStaff` del usuario.
    Quien abre la emergencia queda asignado con la función `Paramedic`. Otro paramédico: `403`;
  - **cualquier otro tipo sin clínica**: nunca accesible.
- `ClinicalActor` no hace consultas: la búsqueda de la asignación está en el read store.
- `EnsureClinic` **no se reemplazó globalmente**: los caminos del hospital (Web BFF) mantienen su regla, y una atención
  sin clínica sigue siendo invisible para ellos. El servicio se conecta uno a uno en los caminos móviles: ya lo usa la
  identificación (decisión 6); faltan signos vitales, procedimientos, medicamentos y traslado.

### 3. Recibos de idempotencia: infraestructura técnica reutilizable

- Tabla `clinical."IdempotencyReceipts"`: `Id`, `ActorUserId`, `Operation`, `ClientRequestId`, `RequestHash`,
  `ResourceId`, `CreatedAtUtc`. Índice único `UX_IdempotencyReceipts_Key` (`ActorUserId`, `Operation`,
  `ClientRequestId`).
- Es **estado técnico de persistencia**, no una entidad del Domain clínico (`Persistence/Clinical/Idempotency`).
  Vive en el schema `clinical` para guardarse en la **misma transacción** que las filas clínicas que protege. No
  tiene FK: `ResourceId` puede apuntar al recurso de cualquier operación.
- `ClinicalEncounter` no tiene `ClientRequestId` ni se aceptan ids generados por el cliente.
- `Operation` la fija el servidor y lleva versión (`prehospital-emergency.open.v1`).
- `RequestHash` es el SHA-256 de los valores lógicos ya normalizados en un orden fijo (`IdempotencyRequestHash`).
  Nunca se usa el JSON crudo, los tokens, la hora del servidor ni metadatos del usuario. En V1 son `startedAt` en UTC y
  `chiefComplaint` recortado.
- Comportamiento:
  - misma clave y mismo hash → se devuelve el recurso del recibo (`200`), sin crear nada;
  - misma clave y otro hash → `409`, sin crear nada;
  - dos envíos simultáneos con la misma clave → el índice único rechaza la segunda transacción completa (atención,
    asignación, auditoría y recibo); el caso de uso captura esa colisión (`IdempotencyKeyCollisionException`), lee el
    recibo ganador y responde como un reintento. Nunca un `500`, ni un duplicado, ni datos huérfanos.
- La abstracción `IIdempotencyStore` (Application) es genérica: los siguientes `POST` móviles la reutilizan con su
  propio nombre de operación.

### 4. Hora de inicio

- `startedAt` la envía el dispositivo y se guarda como `StartedAtUtc`.
- Es un instante: ISO-8601 con `Z` o un offset explícito. Un valor sin offset es ambiguo y se rechaza con `400`: nunca
  se interpreta con la zona horaria del servidor ni se supone UTC. La regla vive en el boundary HTTP (convertidor JSON
  `ExplicitOffsetInstantConverter` del DTO), no en el Domain: tras deserializar un `DateTimeOffset` ya no se puede
  saber si el JSON traía offset.
- Se normaliza a UTC antes de validar, calcular el hash y guardar, así que el mismo instante con otro offset es un
  reintento (`200`), no un conflicto.
- Se acepta desde 24 horas antes hasta 5 minutos después de la hora del servidor (`TimeProvider`), ambos límites
  incluidos; fuera de ese rango, `400`.
- Un reintento de una apertura ya registrada se responde aunque `startedAt` haya quedado fuera del rango.

### 5. Ubicación diferida

La ubicación (latitud/longitud, dirección de la escena) queda **fuera de V1** (FUTURE). No hay columnas para ella. La
app no la envía y el contrato no la acepta: un campo `location` se rechaza con `400`, en lugar de aceptarlo y descartarlo.

### 6. Identificación por documento

- `POST /api/mobile/emergencies/{id}/identify/document` vincula el paciente NN a la **misma** atención reutilizando
  `EncounterService.IdentifyPatientAsync` (regla del Domain, auditoría `Identify`, control de concurrencia sobre
  `PatientId`; el código NN se conserva). No crea pacientes ni atenciones.
- `IdentifyPatientAsync` comprueba el acceso con `IEncounterAccessService` en lugar de `EnsureClinic`: para una
  atención con clínica es la misma regla (el Web BFF no cambia); para una `Prehospital` sin clínica, solo el personal
  asignado.
- **Alcance de plataforma:** el documento se busca con coincidencia exacta sobre identificadores activos con
  `issuer` vacío (alcance SCI-PH). La unicidad del Domain es por `(type, issuer, normalizedValue)`, así que buscar sin
  emisor podría devolver varios pacientes; con el emisor vacío el índice único garantiza 0 o 1. Un identificador con
  otro emisor no se encuentra (`404`). El emisor explícito (país) se añadirá al contrato cuando haga falta.
- Solo `NationalId` y `Passport`: `Other` no demuestra identidad y los demás tipos no se digitan.
- Idempotente con los mismos recibos (`prehospital-emergency.identify-document.v1`); el hash usa el número normalizado
  y el recibo no guarda el documento. Un `404` no guarda nada, para que el mismo intento encuentre al paciente si se
  registra después. Una atención ya identificada responde `409` a cualquier solicitud nueva, aunque sea el mismo
  paciente (regla del Domain sin cambios).

## Consecuencias

- La primera vertical real del paramédico (abrir y leer la emergencia) funciona sin inventar una clínica.
- Una migración clínica (`AddClinicalIdempotencyReceipts`); ninguna de Identity. La identificación por documento no
  añade migraciones. Los recibos se acumulan y no se
  purgan todavía: la retención queda pendiente.
- Tras la llegada al hospital, el paramédico de otra base deja de leer la emergencia, porque se aplica la regla de
  clínica. El acceso posterior al traslado se decidirá con la vertical de traslado.
- `startedAt` puede estar hasta 5 minutos en el futuro, y el Domain exige que las horas clínicas posteriores no sean
  anteriores al inicio. Un registro hecho en esos minutos se rechazaría, así que las próximas verticales deben
  tenerlo en cuenta.
- Solicitar y despachar un traslado de una atención sin clínica todavía no está expuesto (`TransferService` exige
  clínica de origen); llegará con la vertical de traslado usando `IEncounterAccessService`.
