# ADR-008: Emergencia prehospitalaria: acceso sin clínica e idempotencia

- **Estado:** Aceptado (implementado en los PR #25 a #29 y en el de `feature/mobile-prehospital-notes`)
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
  sin clínica sigue siendo invisible para ellos. El servicio se conecta uno a uno en los caminos móviles: ya lo usan la
  identificación (decisión 6), los signos vitales (decisión 7), los procedimientos (decisión 8), los medicamentos
  administrados (decisión 9) y las observaciones (decisión 10); falta el traslado.

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

### 7. Signos vitales

- `POST /api/mobile/emergencies/{id}/vital-signs` registra una toma de 1 a 7 valores (campos fijos, uno por tipo) en una
  transacción: los `VitalSign`, una auditoría por valor, `Created -> InProgress` en la primera toma y el recibo
  (`prehospital-emergency.vital-signs.v1`, recurso: la emergencia). No hay entidad de "toma": la respuesta se reconstruye
  desde la petición canónica y las unidades del servidor.
- El acceso usa `IEncounterAccessService` en un camino propio. La comprobación compartida de `EncounterRecordService`
  (`LoadForRecordingAsync`) no cambia, así que los medicamentos y los signos vitales del Web mantienen la regla de
  clínica hasta su propia vertical.
- `recordedAt` va desde el inicio de la emergencia hasta 5 minutos después de la hora del servidor (el margen del reloj
  del dispositivo ya aceptado para `startedAt`). Nunca se modifica la hora del dispositivo y no se relaja la regla
  global: el Web sigue rechazando horas futuras.
- Con el reloj del dispositivo adelantado, la emergencia puede empezar "después" de la hora del servidor, y el Domain no
  cambia el estado antes del inicio. Por eso la hora de la transición `Created -> InProgress` de este camino (una hora
  del servidor) es `max(ahora, inicio)`.
- Temperatura con hasta 2 decimales (la columna es `numeric(8,2)`): con más, `400`, nunca un redondeo silencioso.

### 8. Procedimientos prehospitalarios

- `POST /api/mobile/emergencies/{id}/procedures` registra **un procedimiento ya realizado** con
  `ClinicalProcedure.RecordCompleted` (sin cambios en el Domain): el procedimiento, su auditoría `Create`,
  `Created -> InProgress` si es el primer registro y el recibo (`prehospital-emergency.procedure.v1`, recurso: el
  `ClinicalProcedure`), en una transacción. El acceso, los estados que admiten registros, la ventana de hora y los tokens
  de concurrencia de la atención son los mismos que en los signos vitales (decisión 7), en un camino compartido.
- **Catálogo de procedimientos prehospitalarios: catálogo fijo controlado por el servidor**, propio del prototipo
  SCI-PH (`PrehospitalProcedureCatalog`, `CodeSystem = "SCI-PH-PREHOSP"`), expuesto en
  `GET /api/mobile/catalogs/procedures`. El cliente solo envía `procedureCode`; el servidor resuelve el nombre, la
  descripción, el sistema y el código. **No es texto libre**: un código fuera del catálogo es un `400`
  (`field: procedureCode`). **Todavía no es un catálogo administrable**: no hay entidad, tabla ni migración, y no hay
  CRUD de administración. Podrá sustituirse después por un catálogo administrado o una terminología estándar con otro
  sistema de códigos; los procedimientos ya registrados conservan su `CodeSystem` y su `Code`, así que un código nunca
  se reutiliza con otro significado.
- El catálogo se protege con `Emergencies.Operate` + `Procedures.Write`: no contiene datos de pacientes y solo sirve para
  registrar un procedimiento; no existe un permiso `Procedures.Read`. Además exige personal clínico activo, como el
  catálogo de medicamentos.
- **Hora:** un solo instante, `performedAt` (`PerformedAtUtc = CompletedAtUtc`), con offset explícito y la ventana de
  la decisión 7 (desde el inicio de la emergencia hasta 5 minutos después de la hora del servidor). Esta vertical no
  registra procedimientos planificados ni en curso.
- **Texto libre:** `outcome` es el resultado que escribe el paramédico (`ClinicalProcedure.Outcome`, máx. 4000). La
  `Description` del procedimiento viene del catálogo, nunca del cliente.
- El hash de idempotencia usa la emergencia, el código, `performedAt` en UTC y el `outcome` recortado (vacío = sin
  resultado). Un reintento se responde desde el recibo y la petición canónica, comprobando de nuevo el acceso.

### 9. Medicamentos administrados

- `POST /api/mobile/emergencies/{id}/medication-administrations` registra **una dosis ya administrada** con
  `MedicationAdministration.Record` (sin cambios en el Domain): la administración, su auditoría `Create`,
  `Created -> InProgress` si es el primer registro y el recibo (`prehospital-emergency.medication-administration.v1`,
  recurso: la `MedicationAdministration`), en una transacción. El acceso, los estados, la ventana de hora y los tokens
  de concurrencia son los del camino compartido de las decisiones 7 y 8.
- **No requiere prescripción:** el paramédico no prescribe. `PrescriptionId` y `PrescriptionDetailId` quedan en `null`
  (el Domain y la base de datos lo admiten) y el contrato rechaza ambos campos; no se usa `Prescriptions.Write`.
- El camino Web (`MedicationService`) **no cambia** y mantiene la regla de clínica. La regla "medicamento inexistente:
  `404`; inactivo: `409`" se comparte con él en un helper, sin cambiar su comportamiento.
- **Catálogo:** `GET /api/mobile/catalogs/medications` (`Emergencies.Operate` + `Medications.Read`, personal clínico
  activo) lista **solo los medicamentos activos**, por nombre y concentración, sin `status`. El `POST` vuelve a comprobar
  el estado, porque un medicamento puede desactivarse después de que la app cargó la lista. La `defaultRoute` es una
  sugerencia para preseleccionar la vía, no una restricción: se acepta cualquier `MedicationRoute`, por su nombre.
- **Precisión de la dosis:** `> 0`, máximo 3 decimales y menor que `1 000 000 000`, validado en Application (`400`,
  `field: dose`): la columna `numeric(12,3)` redondearía sin aviso los decimales de más y desbordaría con valores
  mayores. Solo en este camino móvil; el Web queda igual.
- **Unidad:** texto libre de 1 a 24 caracteres tras recortar, como en el Domain; no hay catálogo de unidades, enum ni
  UCUM, y no se transforma. La lista de la app (mg, mcg, g, mL, UI, gotas, inhalaciones, Otra) es **solo ayuda de UX**,
  no autoridad clínica.
- **Hora:** `administeredAt` con offset explícito y la ventana de la decisión 7.
- El hash de idempotencia usa la emergencia, el medicamento, la dosis canónica (`500 = 500.0`), la unidad recortada, la
  vía, `administeredAt` en UTC y las notas recortadas. Un reintento se responde aunque el medicamento se haya
  desactivado después (la dosis era válida al registrarse), comprobando de nuevo el acceso.

### 10. Observaciones prehospitalarias

- `POST /api/mobile/emergencies/{id}/notes` registra **una observación** con `ClinicalNote.Write` (sin cambios en el
  Domain): la nota, su auditoría `Create` (**sin el texto**), `Created -> InProgress` si es el primer registro y el recibo
  (`prehospital-emergency.note.v1`, recurso: la `ClinicalNote`), en una transacción. El acceso, los estados, la ventana
  de hora y los tokens de concurrencia son los del camino compartido de las decisiones 7 a 9; el camino Web
  (`EncounterRecordService`) no cambia.
- **El tipo lo fija el servidor**: siempre `ClinicalNoteType.Prehospital`; el cliente no lo envía y un campo `type` es un
  `400`. El autor es siempre el personal de la sesión.
- **Contenido inmutable:** las notas no se editan ni se borran; una corrección es otra nota.
- **Límites:** el servidor acepta hasta **8000** caracteres tras recortar (`FieldLengths.NoteContent`, sin cambios); la
  app limita a **4000** como decisión de UX móvil, no como regla clínica.
- **Hora:** `occurredAt` con offset explícito y la ventana de la decisión 7. En la app es **automática y no editable**:
  la hora del primer intento de guardar, conservada en los reintentos.
- El hash de idempotencia usa la emergencia, `occurredAt` en UTC y el texto recortado. La respuesta no expone el autor
  ni `CreatedAtUtc`: se reconstruye desde la petición canónica.
- `ClinicalNote.Write` fija `CreatedAtUtc` con `DateTimeOffset.UtcNow` y no con `TimeProvider`. No afecta al hash, a la
  respuesta ni al reintento; en pruebas no se compara con un valor exacto (P2, sin cambio de Domain).

## Consecuencias

- La primera vertical real del paramédico (abrir y leer la emergencia) funciona sin inventar una clínica.
- Una migración clínica (`AddClinicalIdempotencyReceipts`); ninguna de Identity. La identificación por documento no
  añade migraciones. Los recibos se acumulan y no se
  purgan todavía: la retención queda pendiente.
- Tras la llegada al hospital, el paramédico de otra base deja de leer la emergencia, porque se aplica la regla de
  clínica. El acceso posterior al traslado se decidirá con la vertical de traslado.
- `startedAt` puede estar hasta 5 minutos en el futuro, y el Domain exige que las horas clínicas posteriores no sean
  anteriores al inicio. Los signos vitales, los procedimientos, los medicamentos y las observaciones móviles lo
  resuelven con el mismo margen (decisiones 7 a 10).
- El catálogo de procedimientos es una lista fija del servidor: añadir un procedimiento exige un cambio de código y un
  despliegue, hasta que exista un catálogo administrado.
- `Transferred` sigue sin admitir registros, así que el paramédico no puede registrar procedimientos, medicamentos ni
  observaciones durante el transporte una vez despachado el traslado; queda bloqueado hasta el diseño del traslado y la
  entrega (handoff).
- Un medicamento desactivado justo entre la comprobación y el guardado puede quedar administrado (`Medication` no tiene
  token de concurrencia); se acepta porque la dosis se dio con el catálogo vigente un instante antes.
- Solicitar y despachar un traslado de una atención sin clínica todavía no está expuesto (`TransferService` exige
  clínica de origen); llegará con la vertical de traslado usando `IEncounterAccessService`.
