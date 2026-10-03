# Entidades clínicas de SCI-PH

El código usa nombres en inglés. Todas las tablas están en el schema `clinical`; cuentas y roles siguen en
`identity` y no hay claves foráneas entre ambos. Las decisiones están en
[ADR-005](../adr/ADR-005-clinical-model.md).

## Estructura

```
SCI-PH.Domain/
├── Common/          Guard, ClinicalCode (internos)
├── Constants/       FieldLengths, VitalSignLimits
├── Entities/        Patients, Encounters, Medications, Organizations, Transport, Documents, Auditing
├── Enums/           misma división que Entities
├── Exceptions/      DomainException y excepciones específicas
└── ValueObjects/    TemporaryPatientCode, Sha256Checksum, AuditActor

SCI-PH.Persistence/
├── Configurations/
│   ├── Sql.cs                     SQL de una línea para CHECK e índices filtrados
│   └── Clinical/<Área>/           una IEntityTypeConfiguration<T> por entidad
├── Contexts/        ClinicalDbContext, IdentityDbContext
└── Migrations/      Clinical, Identity
```

## Entidades y relaciones

| Área | Entidad / tabla | Relación principal |
|---|---|---|
| Paciente | `Patient` / `Patients` | Expediente; cuenta opcional por `Guid`, sin FK a Identity |
| Paciente | `PatientIdentifier` / `PatientIdentifiers` | N por paciente; tipo, origen, emisor, estado, principal |
| Historial | `PatientMedicalHistory` / `PatientMedicalHistories` | N versiones por paciente |
| Historial | `PatientAllergy` / `PatientAllergies` | N por versión |
| Historial | `ChronicCondition` / `ChronicConditions` | N por versión |
| Historial | `HomeMedication` / `HomeMedications` | N por versión; medicamento del catálogo opcional |
| Historial | `PatientSurgicalHistory` / `PatientSurgicalHistories` | N por versión |
| Biometría | `PatientBiometricReference` / `PatientBiometricReferences` | N por paciente; solo referencia externa |
| Identificación | `IdentificationAttempt` / `IdentificationAttempts` | Paciente, identificador, referencia biométrica y atención opcionales |
| Institución | `Clinic` / `Clinics` | Centro de salud |
| Personal | `ClinicalStaff` / `ClinicalStaffMembers` | Personal clínico; cuenta opcional |
| Personal | `ClinicStaffAffiliation` / `ClinicStaffAffiliations` | Periodos de trabajo clínica ↔ personal |
| Atención | `ClinicalEncounter` / `ClinicalEncounters` | Paciente opcional (NN), clínica, ambulancia |
| Atención | `EncounterStaffAssignment` / `EncounterStaffAssignments` | Periodos de participación y función |
| Atención | `Triage` / `Triages` | N reevaluaciones por atención |
| Atención | `VitalSign` / `VitalSigns` | N mediciones; triage opcional de la misma atención |
| Atención | `ClinicalNote` / `ClinicalNotes` | Valoración, evolución, prehospitalaria, alta… |
| Atención | `Diagnosis` / `Diagnoses` | Presuntivo o definitivo; código opcional |
| Atención | `ClinicalProcedure` / `ClinicalProcedures` | Planificado → en curso → completado / cancelado |
| Atención | `ClinicalResult` / `ClinicalResults` | Procedimiento opcional de la misma atención |
| Medicación | `Medication` / `Medications` | Catálogo |
| Medicación | `Prescription` / `Prescriptions` | Atención (paciente derivado) y prescriptor |
| Medicación | `PrescriptionDetail` / `PrescriptionDetails` | Medicamento, dosis, vía, frecuencia, duración |
| Medicación | `MedicationAdministration` / `MedicationAdministrations` | Dosis real; receta opcional de la misma atención |
| Transporte | `Ambulance` / `Ambulances` | Unidad; clínica base opcional |
| Transporte | `ClinicalTransfer` / `ClinicalTransfers` | Atención, origen, destino, ambulancia, responsables |
| Documentos | `ClinicalDocument` / `ClinicalDocuments` | Exactamente un dueño: paciente **o** atención |
| Auditoría | `ClinicalAuditEntry` / `ClinicalAuditEntries` | Sin FKs; actor usuario, sistema o dispositivo |

Todas las relaciones usan `ON DELETE RESTRICT`.

## Flujos principales

**Paciente conocido.** `ClinicalEncounter.CreateForPatient(patientId, tipo, inicio, clinicId…)`. No genera
código NN.

**Paciente NN.**
1. `ClinicalEncounter.CreateUnidentified(Emergency | Prehospital, inicio…)` genera `NN-<32 hex>` único.
2. Se registra todo sobre la atención: triage, signos, notas, procedimientos, resultados, documentos,
   medicación administrada e incluso recetas.
3. La identidad se resuelve (QR, cédula, huella, rostro, manual) y cada intento queda en
   `IdentificationAttempt`. Un intento exitoso no vincula la atención por sí solo.
4. `IdentifyPatient(patientId, staffId, fechaUtc)` vincula la misma atención y conserva el código NN. Guardar
   en la misma transacción una `ClinicalAuditEntry`. Si hay `DbUpdateConcurrencyException`, revisar la
   vinculación existente; nunca sobrescribirla.

**Traslado.** `ClinicalTransfer.Request → Dispatch → Receive → Complete` (o `Cancel` antes de recibir).
Application cambia en la misma transacción el estado de la atención (`Transferred`, `Received`).

## Restricciones principales

| Tipo | Restricción |
|---|---|
| Único filtrado | `UX_PatientIdentifiers_ActiveValue`: cédula/pasaporte/QR/código interno activos por tipo + emisor + valor |
| Único filtrado | `UX_PatientIdentifiers_ActivePrimary`: un principal activo por paciente y tipo |
| Único filtrado | `UX_ClinicStaffAffiliations_Active`, `UX_EncounterStaffAssignments_Active`: un periodo abierto |
| Único | `TemporaryPatientCode`, `BlobPath`, `FacilityCode`, licencia, código de ambulancia, código de medicamento, `IdentityUserId` de paciente y personal, `(PatientId, Version)` del historial, referencia biométrica por proveedor |
| CHECK | Identificación NN, clínica obligatoria salvo prehospital, fin de atención coherente con estado |
| CHECK | Traslado: origen ≠ destino, fechas ordenadas, responsables y ciclo de vida |
| CHECK | Documento: un solo dueño, tamaño > 0, checksum SHA-256, ciclo de vida |
| CHECK | Intento de identificación: resultado/paciente/motivo, evidencia según método, actor obligatorio |
| CHECK | Auditoría: actor según tipo, motivo solo en fallos |
| CHECK | Signos vitales: valor plausible y unidad canónica por tipo |
| CHECK | Un CHECK de valores permitidos por cada columna enum |
| FK compuesta | Signo vital → triage, resultado → procedimiento, administración → receta / detalle, intento → identificador / referencia biométrica |

## Almacenamiento

- Archivos: solo metadata y `BlobPath` en PostgreSQL; el archivo vive en object storage (ADR-004).
- Biometría: solo la referencia del proveedor; nunca imágenes, huellas ni plantillas.
- Auditoría: `ChangesJson` en `jsonb`, con datos sensibles redactados antes de escribir.
