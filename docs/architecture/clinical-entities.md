# Entidades clínicas de SCI-PH

Se mantiene la nomenclatura inglesa del código. Todas las tablas de esta lista
pertenecen al schema `clinical`. Las cuentas y roles continúan en `identity`.

| Grupo | Entidad / tabla | Relación principal |
|---|---|---|
| Paciente | `Patient` / `Patients` | Persona; cuenta opcional por `Guid`, sin FK a Identity |
| Identificación | `PatientIdentifier` / `PatientIdentifiers` | Muchos identificadores por paciente |
| Historial | `PatientMedicalHistory` / `MedicalHistories` | Varias versiones por paciente |
| Historial | `PatientAllergy` / `Allergies` | Alergias por versión de historial |
| Historial | `ChronicCondition` / `ChronicConditions` | Enfermedades crónicas por versión |
| Historial | `HomeMedication` / `HomeMedications` | Medicación habitual por versión; medicamento del catálogo opcional |
| Historial | `PatientSurgicalHistory` / `SurgicalHistories` | Cirugías previas por versión |
| Biometría | `PatientBiometricReference` / `PatientBiometricReferences` | Referencias externas de enrolamiento por paciente |
| Biometría | `BiometricIdentificationAttempt` / `BiometricIdentificationAttempts` | Atención, operador y paciente coincidente opcionales |
| Auditoría | `ClinicalAuditEntry` / `ClinicalAuditEntries` | Actor, entidad y acción; paciente y atención opcionales |
| Institución | `Clinic` / `Clinics` | Centro de salud |
| Personal | `ClinicalStaff` / `ClinicalStaff` | Personal clínico; cuenta opcional |
| Afiliación | `ClinicStaffAffiliation` / `ClinicStaffAffiliations` | Une clínica y personal (muchos a muchos) |
| Atención | `ClinicalEncounter` / `ClinicalEncounters` | Consulta, urgencia o atención prehospitalaria; paciente opcional inicialmente |
| Equipo | `EncounterStaffAssignment` / `EncounterStaffAssignments` | Personal asignado a una atención |
| Triaje | `TriageAssessment` / `TriageAssessments` | Evaluaciones por atención; no obligatorio en consultas |
| Mediciones | `VitalSign` / `VitalSigns` | Atención obligatoria; triaje opcional de esa misma atención |
| Notas | `ClinicalNote` / `ClinicalNotes` | Texto clínico por atención y autor |
| Diagnóstico | `EncounterDiagnosis` / `EncounterDiagnoses` | Diagnósticos por atención, con sistema y código opcionales |
| Procedimiento | `ClinicalProcedure` / `ClinicalProcedures` | Intervenciones por atención, ejecutor, estado y resultado |
| Resultado | `ClinicalResult` / `ClinicalResults` | Atención; procedimiento opcional de esa misma atención |
| Documento | `ClinicalDocument` / `ClinicalDocuments` | Paciente, atención o ambos; solo metadata de almacenamiento |
| Transporte | `Ambulance` / `Ambulances` | Unidad; clínica propietaria opcional |
| Traslado | `ClinicalTransfer` / `ClinicalTransfers` | Atención, origen, destino y ambulancia |
| Catálogo | `Medication` / `Medications` | Medicamento asignable a recetas, historial y administraciones |
| Receta | `Prescription` / `Prescriptions` | Paciente, atención y prescriptor |
| Receta | `PrescriptionItem` / `PrescriptionItems` | Medicamento, indicaciones y cantidades prescritas por receta |
| Administración | `MedicationAdministration` / `MedicationAdministrations` | Dosis administrada, atención y personal; funciona en atención NN |

## Flujo NN

1. Crear `ClinicalEncounter` sin paciente. Se genera `NN-<Guid>` y el estado es
   `Unidentified`. La clínica puede quedar vacía durante atención prehospitalaria.
2. Registrar la atención usando su `Id`: notas, signos, intervenciones,
   resultados, documentos y medicamentos administrados.
3. Resolver la identidad por el mecanismo correspondiente. Un resultado
   biométrico se registra como intento; no vincula por sí solo a la persona.
4. Invocar `IdentifyPatient(patientId, staffId, fechaUtc)` y agregar la entrada de
   auditoría en el mismo `SaveChanges`/transacción. No crear otra atención ni mover
   sus registros. Resolver cualquier `DbUpdateConcurrencyException` revisando
   la vinculación existente, sin sobrescribirla automáticamente.
5. Una ficha provisional puede asociarse con estado `Provisional`; para confirmar
   la misma persona se usa `IdentifyPatient`. Una corrección de persona equivocada
   exige un flujo aparte de revisión y auditoría.

## Archivos clínicos

`ClinicalDocument` contiene `FileName`, `BlobPath`, `ContentType`, `SizeBytes`,
`Type`, `Status`, `ChecksumSha256`, fechas y autor. El backend genera la ruta del
objeto y calcula su hash. La base no almacena archivos binarios ni URLs públicas
permanentes. La descarga autorizada y el manejo de cargas fallidas pertenecen
al servicio de documentos, aún pendiente.

Las reglas adoptadas y las comprobaciones que quedan para Application se
detallan en [ADR-005](../adr/ADR-005-clinical-model.md).
