# ADR-005: Modelo de datos clínico

- **Estado:** Propuesto para revisión en el PR de `feature/entities`
- **Fecha:** 2026-10-02
- **Alcance:** `SCI-PH.Domain`, configuraciones de `ClinicalDbContext` y migración `InitialClinical`

## Contexto

Antes de construir funcionalidades se necesita una base de datos clínica estable: entidades, reglas que no
dependan de cada pantalla y restricciones en PostgreSQL que protejan la información aunque falle una capa
superior. El detalle de cada entidad está en [clinical-entities.md](../architecture/clinical-entities.md).

## Decisiones

### 1. Organización y convenciones

- Domain se organiza por área (`Patients`, `Encounters`, `Medications`, `Organizations`, `Transport`,
  `Documents`, `Auditing`) con una clase por archivo; los enums siguen la misma división.
- Identificadores `Guid`; fechas `DateTimeOffset` en UTC con sufijo `Utc`. `CreatedAtUtc`/`UpdatedAtUtc`
  los asigna el dominio; las horas clínicas (`StartedAtUtc`, `MeasuredAtUtc`, `OccurredAtUtc`…) las indica
  quien registra.
- Las entidades exponen `private set` y se crean con métodos de fábrica (`Register`, `Record`,
  `CreateForPatient`…). Solo las entidades con reglas reales tienen comportamiento; no hay agregados complejos,
  eventos de dominio ni repositorios genéricos.
- Todos los enums tienen valores numéricos explícitos que empiezan en 1, para que el valor por defecto `0`
  nunca sea válido. Cada columna enum tiene un CHECK con los valores permitidos (`CK_<Tabla>_<Columna>_Enum`);
  agregar un miembro exige una migración.
- Value objects solo donde validan algo real: `TemporaryPatientCode`, `Sha256Checksum` y `AuditActor`.
- Excepciones de dominio: `DomainException` y cuatro específicas (`PatientAlreadyIdentifiedException`,
  `InvalidClinicalEncounterStateException`, `InvalidClinicalTransferException`,
  `InvalidPatientIdentifierException`). Los argumentos inválidos usan las excepciones estándar de `Argument*`.

### 2. Paciente, cuenta e identificadores

- `Patient` es el expediente clínico dentro de SCI-PH, no una cuenta ni un registro nacional oficial.
  `IdentityUserId` es opcional y no tiene FK hacia `identity`; un paciente existe sin cuenta.
- `PatientIdentifier` guarda tipo, valor original, valor normalizado, emisor, **origen** (`Manual`, `Ocr`,
  `QrScan`, `ExternalImport`, `System`), estado, verificación, principal y vigencia.
- Cédula, pasaporte, QR y código interno **activos** son únicos por tipo, emisor y valor normalizado
  (`UX_PatientIdentifiers_ActiveValue`). Un QR activo no puede pertenecer a dos pacientes. Revocar libera el
  valor. Teléfono y email pueden compartirse: una coincidencia no prueba identidad.
- Solo un identificador principal activo por paciente y tipo (`UX_PatientIdentifiers_ActivePrimary`).
- La caducidad se evalúa con `ValidUntilUtc` al usar el identificador; no se guarda como estado.

### 3. Atención clínica y paciente NN

- `ClinicalEncounter.CreateForPatient` crea la atención de un paciente conocido: `PatientId` informado y sin
  código temporal.
- `ClinicalEncounter.CreateUnidentified` solo se permite en `Emergency` y `Prehospital`: `PatientId` nulo y un
  `TemporaryPatientCode` único con formato `NN-<32 hex>`.
- `IdentifyPatient` vincula **la misma** atención; todos sus registros clínicos quedan en su lugar porque
  cuelgan de la atención. El código NN **se conserva** como referencia histórica y se registran
  `IdentifiedAtUtc` e `IdentifiedByStaffId`. Una atención ya vinculada no se reasigna: eso requiere otro flujo
  revisado. `CK_ClinicalEncounters_Identification` acepta solo los tres estados válidos (NN pendiente,
  paciente conocido, NN identificado).
- Estados: `Created → InProgress → (Transferred → Received) / InEvolution → Closed`, y `Cancelled` desde
  `Created`/`InProgress`. `Closed` y `Cancelled` son finales y fijan `EndedAtUtc`.
- `PatientId` y `Status` son tokens de concurrencia: dos canales que identifiquen o cambien el estado a la vez
  producen `DbUpdateConcurrencyException` en lugar de sobrescribirse.
- La clínica es obligatoria salvo en atención prehospitalaria. El acceso a una atención prehospitalaria sin clínica
  (por asignación) y la idempotencia de su apertura se deciden en [ADR-008](ADR-008-prehospital-emergency.md).

### 4. Registros de la atención

- Triage, signos vitales, notas, diagnósticos, procedimientos y resultados son filas propias de la atención
  (historial completo; nada se sobrescribe dentro de `ClinicalEncounter`).
- Los signos vitales usan tipo + valor + unidad canónica (UCUM). Los rangos de plausibilidad están en
  `VitalSignLimits`; el dominio los valida y la misma tabla genera `CK_VitalSigns_Range`.
- Claves foráneas compuestas impiden vínculos cruzados: signo vital → triage, resultado → procedimiento y
  administración → receta deben pertenecer a la misma atención.

### 5. Medicación

- `Medication` es solo catálogo.
- `Prescription` pertenece siempre a una atención y **no** guarda `PatientId`: el paciente se deriva de la
  atención, así no puede haber dos pacientes contradictorios. Por ello también se puede indicar medicación a
  un NN.
- `MedicationAdministration` es una dosis real administrada; se corrige marcándola `EnteredInError`, nunca
  borrándola. `HomeMedication` es la medicación habitual dentro del historial y no se mezcla con ella.

### 6. Historial clínico

- `PatientMedicalHistory` es una versión inmutable con alergias, condiciones crónicas, medicación habitual y
  antecedentes quirúrgicos. Una actualización crea la siguiente versión; Application calcula el número y el
  índice único `(PatientId, Version)` rechaza duplicados concurrentes.

### 7. Personal, clínicas y traslados

- `ClinicStaffAffiliation` y `EncounterStaffAssignment` guardan periodos. Solo el periodo **abierto** es único
  (índices filtrados por `EndedAtUtc IS NULL` / `UnassignedAtUtc IS NULL`), así una persona puede salir y
  volver.
- `FacilityCode`, licencia profesional y código de ambulancia son únicos cuando existen.
- `ClinicalTransfer` sigue `Requested → InTransit → Received → Completed` o `Cancelled` antes de la recepción,
  con fechas y responsables de envío y recepción. CHECK: origen distinto del destino, fechas en orden y
  campos coherentes con cada estado. Application actualiza en la misma transacción el estado de la atención.

### 8. Documentos clínicos

- Solo metadata y ruta del objeto (ADR-004); no hay columnas binarias.
- Un documento tiene **exactamente un dueño**: el paciente o la atención (`CK_ClinicalDocuments_Owner`,
  XOR). Si pertenece a una atención, el paciente se deriva de ella; los documentos de un NN quedan
  vinculados automáticamente al identificarlo.
- `Available` exige checksum SHA-256 y fecha de carga; el borrado es lógico (`Deleted` + `DeletedAtUtc`).

### 9. Biometría e intentos de identificación

- `PatientBiometricReference` guarda solo la referencia del proveedor externo, su estado y revocación; nunca
  imágenes, huellas ni plantillas.
- `IdentificationAttempt` registra cualquier método (QR, cédula, pasaporte, código interno, teléfono, rostro,
  huella, manual) con resultado `Success`/`Failure`, motivo de fallo, dispositivo y actor. Un éxito exige
  paciente; el identificador o la referencia biométrica coincidente deben pertenecer a ese paciente (FK
  compuesta). Un intento exitoso **no** vincula la atención por sí solo.
- La identificación por huella en emergencias usa ambas entidades sin cambiarlas: referencias `R307` por lector y
  slot, un intento por resultado del lector y la confirmación del paramédico ([ADR-009](ADR-009-fingerprint-identification.md)).
  El registro de huellas crea esas referencias desde el Web ([ADR-010](ADR-010-fingerprint-enrollment.md)); la unicidad
  de la referencia del proveedor solo aplica a las activas, para conservar la historia de las revocadas.

### 10. Auditoría

- `ClinicalAuditEntry` admite actores `User`, `System` y `Device` (`ActorIdentityUserId` opcional,
  `ActorReference` para sistema/dispositivo) y resultado `Success`/`Failure` con motivo.
- No tiene FKs: el log acepta entradas sobre cualquier fila y nunca bloquea su ciclo de vida.
- Es append-only: `SaveChanges`/`SaveChangesAsync` rechazan modificar o borrar entradas. **No usar**
  `ExecuteUpdate`, `ExecuteDelete` ni SQL directo sobre auditoría ni sobre entidades auditables sin registrar la
  auditoría explícitamente. Los permisos de base de datos del escritor de auditoría se definen antes de
  producción. Redactar datos sensibles antes de escribir `ChangesJson`.

### 11. Persistencia

- Una `IEntityTypeConfiguration<T>` por entidad en `Configurations/Clinical/<Área>`. `ClinicalDbContext` solo
  declara `DbSet`, el schema `clinical` y aplica las configuraciones de entidades de Domain (las de Identity
  quedan excluidas).
- Todas las FKs usan `Restrict` mediante una convención global; no queda ningún `Cascade`.
- No hay FKs entre `clinical` e `identity`; `IdentityDbContext` y sus migraciones no cambian.
- El SQL de CHECK e índices filtrados se genera en una sola línea a partir de `nameof` y de los enums
  (`Configurations/Sql.cs`). Así el modelo es idéntico en checkouts LF y CRLF y no aparecen cambios pendientes
  falsos (problema detectado con un raw string multilínea en la revisión anterior). `.gitattributes`
  normaliza el repositorio a LF como refuerzo.

## Consecuencias

- La base rechaza estados inválidos aunque falle la validación de una capa superior.
- Agregar un valor a un enum o cambiar un rango de signos vitales exige una migración.
- Algunas reglas entre filas quedan en Application: avanzar el estado de la atención junto con el traslado,
  numerar versiones del historial y registrar auditoría en la misma transacción.
- Las pruebas actuales validan el modelo, el DDL generado y la coherencia snapshot/modelo sin base de datos.
  Las pruebas contra PostgreSQL 17 real (Testcontainers) quedan para el siguiente PR.
- `InitialClinical` no se ha aplicado a ninguna base compartida.

Referencias: [índices](https://learn.microsoft.com/ef/core/modeling/indexes),
[concurrencia](https://learn.microsoft.com/ef/core/saving/concurrency) y
[restricciones CHECK](https://learn.microsoft.com/ef/core/modeling/indexes#check-constraints) en EF Core.
