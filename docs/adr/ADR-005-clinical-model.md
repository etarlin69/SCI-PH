# ADR-005: Identificación e integridad del modelo clínico

- **Estado:** Propuesto para revisión del equipo en el PR de `feature/entities`.
- **Fecha:** 2026-10-01.
- **Base revisada:** archivo de la rama `feature/entities`, commit `2ea944711173d601cb4f6cf4038309bf2b6af11d`.

## Decisiones

1. Se conservan las entidades existentes. `clinical` contiene los datos clínicos;
   `identity` conserva autenticación, cuentas y roles. Los IDs de cuentas en el
   dominio se cambian a `Guid`, como `ApplicationUser : IdentityUser<Guid>`.
   No se agregan navegaciones ni claves foráneas entre los dos contextos.
2. `PatientIdentifier` incluye tipo, valor original, valor normalizado, emisor,
   verificación, prioridad y vigencia. La aplicación normaliza el valor y el
   emisor antes de guardar. El emisor vacío representa el ámbito nacional de la
   plataforma; los pasaportes deben indicar el país emisor y los códigos locales
   deben indicar su ámbito. Cédula, pasaporte, QR y código interno no revocados son
   únicos por tipo, emisor y valor normalizado. Teléfono, email y otros valores
   pueden compartirse; una coincidencia no prueba identidad. La caducidad se
   valida al usar el identificador; el índice no depende de la fecha actual.
3. Una atención NN nace con `PatientId = null`, estado `Unidentified` y un código
   temporal. Las notas, signos, procedimientos, resultados, documentos y
   administraciones se relacionan con la atención. `IdentifyPatient` conserva
   esa atención y su código; registra paciente, personal y fecha de identificación.
   La aplicación debe registrar la auditoría en la misma transacción. El campo
   `PatientId` es un token de concurrencia para detectar vinculaciones simultáneas
   mediante EF. Reasignar o fusionar pacientes requiere otro flujo revisado.
4. El historial estructurado conserva alergias, enfermedades crónicas, medicación
   habitual y antecedentes quirúrgicos por versión. La aplicación asigna la
   siguiente versión por paciente y resuelve conflictos del índice único.
   No debe sumar distintas versiones como si todas fueran el estado actual.
5. `PatientBiometricReference` guarda el identificador externo de enrolamiento y
   su revocación. `BiometricIdentificationAttempt` guarda el intento, resultado,
   proveedor, atención y paciente coincidente. La referencia coincidente, cuando
   existe, pertenece al mismo paciente. No se guardan muestras ni plantillas en
   estas tablas. El proveedor de biometría y las reglas de verificación siguen
   pendientes de implementar.
6. La auditoría es una entidad explícita. `SaveChanges` y `SaveChangesAsync`
   rechazan modificar o eliminar entradas existentes. Esto no protege contra
   SQL directo ni `ExecuteUpdate/ExecuteDelete`: antes de producción deben
   definirse permisos de base de datos y el escritor de auditoría, incluyendo
   accesos de lectura. No se serializan automáticamente expedientes en JSON.
7. Los documentos almacenan metadata y ruta de blob. Deben pertenecer a un
   paciente, una atención o ambos, tener tamaño no negativo y checksum SHA-256
   válido si se proporciona. `Available` requiere checksum y fecha de carga.
   El contenido físico permanece fuera de PostgreSQL según ADR-004. El servicio
   de almacenamiento debe calcular y verificar el checksum real; una expresión
   regular solo valida su formato.
8. Signos vitales asociados a triage y resultados asociados a procedimientos
   deben corresponder a la misma atención, mediante claves foráneas compuestas.
   La eliminación en cascada de datos clínicos queda restringida.

## Consecuencias y límites de esta entrega

- Se prepara el modelo y su persistencia; todavía no hay endpoints ni un flujo
  funcional de identificación biométrica o de descarga de documentos.
- Una receta requiere paciente y atención. Durante una atención NN se pueden
  registrar medicamentos administrados; una receta se emite tras vincular al
  paciente. No se infiere dispensación, consumo ni existencias por el paso del tiempo.
- `Prescription.PatientId` y `ClinicalDocument.PatientId`, si se proporcionan
  junto con una atención, deben coincidir con el paciente de esa atención. Esta
  comprobación entre filas corresponde al servicio de aplicación en la misma
  transacción; no está cubierta por un CHECK de PostgreSQL.
- Las propiedades de entidades siguen siendo públicas para mantener la base
  existente. Los CHECK cubren consistencia de estados; no sustituyen los permisos
  ni la autorización para identificar, corregir o consultar un paciente.
- Se conserva `ClinicalStaffRole` como clasificación clínica. Los permisos se
  administran en Identity. No se agrega personal administrativo no clínico.
- Antes de aplicar una migración clínica, el equipo debe revisar estas decisiones
  en el PR. No se ha aplicado nada a una base compartida.

Referencias técnicas: [índices y CHECK en EF Core](https://learn.microsoft.com/ef/core/modeling/indexes)
y [concurrencia en EF Core](https://learn.microsoft.com/ef/core/saving/concurrency).
