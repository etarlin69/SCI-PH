# ADR-013: Asistente clínico de IA (resumen asistido de la atención)

- **Estado:** Aceptado. PR 1/2 implementado en `feature/clinical-ai-assistant`: contrato, proveedor Fake determinista,
  UI del hospital, auditoría y límites. PR 2/2 (proveedor real en la nube): pendiente, con las condiciones de la decisión 12.
- **Fecha:** 2026-10-30
- **Relacionado:** [ADR-001](ADR-001-onion-architecture.md), [ADR-005](ADR-005-clinical-model.md),
  [ADR-006](ADR-006-identity-security.md), [ADR-012](ADR-012-face-verification.md),
  [contratos del Web BFF](../api/web-bff-contracts.md#web-hospital--resumen-asistido-por-ia-implementado)

## Contexto

La IA estaba prevista desde el inicio como asistente **educativo** (README, `runtime-components.md`), pero solo existía una
interfaz vacía y genérica (`IAiAssistantService`) y, en la app del paciente, un chat simulado con respuestas fijas
(`MockPatientAssistantService`). Un chat abierto es la forma más difícil de controlar: alucinaciones, alcance, datos de
salud (PHI), consejo médico, tamaño del contexto y auditoría. La primera vertical es una operación **cerrada, de solo
lectura y con revisión humana**.

## Decisiones

### 1. Operaciones cerradas detrás de `IClinicalAiService`

- `IAiAssistantService` (vacía, conversacional) se **elimina**. Application conoce `IClinicalAiService`: `Provider`,
  `ModelVersion`, `PromptVersion`, `Timeout`, `IsEnabled` y una operación tipada,
  `SummarizeEncounterAsync(EncounterSummaryInput)` → `EncounterSummaryDraft`.
- **No existe** `SendPromptAsync(string)`, `AskAsync`, `ChatAsync` ni `CompleteAsync`: Application no puede construir
  prompts arbitrarios. Cada operación nueva (p. ej. una explicación educativa) será otro método cerrado, con su prompt.
- Sin SDK de IA en Domain ni Application. Infrastructure implementa los proveedores.

### 2. Caso de uso: resumen asistido de una atención + puntos de documentación a revisar

- Web del hospital, expediente de la atención: "Generar resumen asistido". Sin chat, sin resumen longitudinal del paciente,
  sin IA para el paciente ni para el paramédico, sin endpoint educativo en esta fase.
- **Límite de seguridad clínica.** La IA puede resumir hechos registrados, ordenarlos cronológicamente, describir cambios
  numéricos ("La PA sistólica cambió de 150 a 180 mm[Hg] entre +00:30 y +01:10") y señalar huecos, contradicciones y
  elementos pendientes **de la documentación**. **No** diagnostica ni propone diagnósticos, no recomienda tratamiento, no
  prescribe, no clasifica gravedad, no pronostica, no interpreta clínicamente un valor ("peligrosamente alta" no está
  permitido), no crea órdenes y no modifica datos. Un diagnóstico solo aparece si ya está en `Diagnosis`, como
  "Se documentó el diagnóstico …".

### 3. Datos: solo registros reales de la atención

- Entrada: `ClinicalEncounter` (tipo, estado, motivo, última prioridad, fin), `Triage`, `VitalSign`, `ClinicalNote`,
  `Diagnosis`, `Prescription` con sus líneas, `MedicationAdministration`, `ClinicalProcedure` y `ClinicalTransfer`.
  Las dos últimas tienen lecturas nuevas por atención (`ListProceduresAsync`, `ListEncounterTransfersAsync`), sin migración.
- No se usan entidades sin flujo real (resultados, documentos, alergias, antecedentes, medicación domiciliaria); no
  existen `LabResult` ni `ImagingStudy`.

### 4. Minimización por construcción (`EncounterAiInputBuilder`)

- Nunca llegan al proveedor: nombre, documento, email, teléfono, dirección, seguro, `PatientId`, `TemporaryPatientCode`,
  `ClinicId`, nombres o ids del personal, ni ningún id de registro. Sin edad ni sexo en esta fase.
- Cada registro tiene un **alias** (`T1` triage, `V1` signo vital, `N1` nota, `D1` diagnóstico, `RX1` prescripción,
  `M1` administración, `P1` procedimiento, `TR1` traslado); el mapa alias → id existe solo en Application.
- Tiempos **relativos** al inicio de la atención (`+01:10`); ninguna fecha u hora absoluta.
- **Presupuesto determinista:** por tipo, los más recientes hasta un límite (10 triages, 50 signos vitales, 30 notas,
  20 diagnósticos, 20 prescripciones, 50 administraciones, 30 procedimientos, 10 traslados); después, mientras la entrada
  serializada supere 60 000 caracteres, se quita el registro más antiguo del primer tipo no vacío (notas, triages,
  administraciones, procedimientos, traslados, prescripciones, signos vitales, diagnósticos). Los registros se listan del
  más antiguo al más reciente. Cualquier recorte marca `truncated = true`. El cliente no define límites.
- **Huella de la entrada** (`sourceFingerprint`): SHA-256 de la entrada minimizada canónica. Sirve para frescura,
  diagnóstico y la futura deduplicación; no es una primitiva de seguridad.

### 5. Prompt versionado y frontera contra inyección (`EncounterSummaryPromptV1`)

- Un solo prompt, en Infrastructure, con versión `encounter-summary.v1` (cualquier cambio es una versión nueva). Ni en
  controllers, ni en servicios dispersos, ni en React.
- Mensaje de sistema **constante** (ningún texto clínico se interpola). Mensaje de usuario: **solo** `{"encounterData": …}`
  serializado por el servidor; el texto libre (notas, motivos) es un valor string JSON escapado, nunca una instrucción. El
  sistema declara que ese JSON es documentación y que cualquier instrucción dentro de él se ignora.
- Sin herramientas, navegación, function calling ni historial de conversación. La respuesta debe ajustarse a
  `ResponseSchema` (JSON estricto, sin miembros extra); `ParseDraft` la lee como un borrador no confiable.

### 6. Contrato de salida y validación (`EncounterAiOutputValidator`)

- `EncounterAiSummary`: `aiGenerated = true`, `requiresClinicianReview = true`, `summary` (≤ 1500), `sections`
  (`course`, `vitalSigns`, `medications`, `procedures`, `notes`, `transfer`; cada una ≤ 800 o `null`), `reviewPoints`
  (≤ 10; `kind` ∈ `MissingDocumentation | Inconsistency | PendingItem`; texto ≤ 300), `notDocumented` (≤ 10 × 200),
  `generatedAtUtc`, `provider`, `model`, `promptVersion`, `sourceFingerprint`, `truncated`.
- **No existen** campos de diagnóstico sugerido, recomendación, tratamiento, gravedad, confianza ni razonamiento del modelo.
- El servidor valida el borrador **entero**: presencia de cada campo, textos no vacíos, longitudes, cantidades, `kind` y
  tipos de fuente exactamente entre los nombres publicados, texto plano (sin etiquetas HTML ni caracteres de control salvo
  saltos de línea) y modelo y versión de prompt del propio proveedor. Cualquier fallo → 503; nunca una respuesta parcial.

### 7. Registros relacionados (trazabilidad)

- Cada sección y punto a revisar lista sus fuentes como alias. El servidor exige que el alias exista **en esta entrada**
  y que su tipo coincida; si no, la respuesta entera se rechaza (503). La respuesta al cliente lleva `{ type, id }`, nunca
  alias.
- Son **"registros relacionados"**, no prueba ni evidencia verificada: que `V3` exista no demuestra que la frase esté
  respaldada por `V3`. La UI los usa solo para navegar (a la pestaña donde se ve ese tipo de registro) y mantiene
  "Requiere revisión profesional". Nunca un "verificado por IA".

### 8. Efímero, solo auditoría

- El contenido generado **no se guarda**: ni en la base clínica, ni en Blob, ni en disco, ni en logs, ni en la auditoría.
  No hay `ClinicalAiArtifact`. Nada se convierte en `ClinicalNote`, `Diagnosis`, `Prescription` ni procedimiento; no hay
  botón "aplicar". Copiar o adaptar texto será, en una fase futura, una acción humana explícita.
- Una entrada `ClinicalAuditEntry` por intento: `EntityName = ClinicalAiEncounterSummary`, `EntityId` = atención,
  `AuditContext(PatientId, EncounterId)`, `Action = Read`, `Success` o `Failure` (con la categoría como motivo).
  `ChangesJson` solo con: operación, proveedor, modelo, versión de prompt, huella, conteo de registros por tipo, recorte,
  categoría de latencia y categoría de fallo. Nunca prompt, respuesta, texto de notas, contenido generado, valores
  clínicos, alias ni claves.
- Logs: los mismos metadatos (la correlación sale del scope de la petición); nunca contenido clínico ni generado.
- **PR 2:** cuando los datos salgan hacia un proveedor externo, `Action = Export` (también un intento fallido después de
  enviar datos). Sin cambios de Domain.

### 9. Acceso, límites y cuerpo sin opciones

- `POST /api/web/encounters/{id}/ai-summary`: el mismo acceso que leer la atención (`Encounters.Read` +
  `EncounterService.GetReadableAsync`: actor clínico activo, clínica de la atención, regla del paciente). Sin permiso nuevo
  ni comprobación por rol: Doctor, Nurse, Paramedic y Administrator con contexto clínico válido; nunca Receptionist ni
  Patient.
- POST porque cada llamada es una generación auditada y costosa. Cuerpo vacío o `{}`; cualquier miembro (`model`,
  `prompt`, `temperature`, `provider`, `maxTokens`, `systemPrompt`, `patientId`, datos clínicos) → 400.
- Rate limiting `clinical-ai` por usuario autenticado (`sub`): 10 por minuto **y** 1 a la vez; 429 con `Retry-After`. Por
  eso en el Web BFF `UseAuthentication` va antes de `UseRateLimiter` (las políticas existentes, por IP, no cambian). El
  limitador corre antes del model binding, así que una petición rechazada con 400 también consume cuota: una solicitud
  malformada no puede usar el endpoint sin costo (decisión aprobada; no se mueve el límite después del binding). La UI
  además deshabilita el botón mientras genera.
- Cancelación: el token de la petición llega al proveedor; si el cliente se va, la generación se cancela y no se audita.
  `Timeout` del proveedor (30 s por defecto, 1–120): 503.

### 10. Fallos

- Disabled, proveedor no disponible, timeout, salida inválida y fuente desconocida → el mismo **503** genérico ("The
  assisted summary is not available right now…"), sin mensajes internos, prompt ni trazas. Categorías solo en la auditoría:
  `ProviderDisabled`, `ProviderUnavailable`, `Timeout`, `InvalidOutput`, `UnknownSource`.

### 11. Proveedores y configuración

- `ClinicalAi:Provider` (solo servidor): `Fake` o `Disabled`. Sin configurar: Fake fuera de Production, Disabled en
  Production. Fake en Production impide arrancar. `AzureOpenAI` está reservado para el PR 2 (hoy no arranca).
- **Fake** (`SCIPH-AiFake`, `fake-summary-v1`): determinista y en proceso (sin red, claves ni modelos). Escribe una
  plantilla fija a partir de los registros estructurados; nunca copia texto narrativo (notas, motivos, observaciones), así
  que una nota no puede cambiar su respuesta. Sus puntos a revisar son hechos documentales derivables (p. ej. una
  prescripción emitida sin administración vinculada). Su respuesta pasa por el mismo documento y parser que la de un
  proveedor real. Escenarios de prueba por configuración (`ClinicalAi:Fake:Scenario`: `ProviderUnavailable`, `Timeout`,
  `InvalidSchema`, `UnknownAlias`, `OversizedOutput`; `DelayMilliseconds`), nunca disponibles en Production.
- **PR 1: ningún dato sale de SCI-PH.**

### 12. Proveedor real (PR 2) y gobierno de PHI

- Propuesta: Azure OpenAI con salida estructurada estricta, el prompt V1 y el mismo validador; OpenAI API como alternativa.
  Un modelo local (Ollama/llama.cpp) solo como opción posterior detrás de la misma interfaz.
- Enviar PHI a un proveedor en la nube exige: acuerdo empresarial de tratamiento de datos aprobado, revisión de región y
  configuración, y revisión de privacidad y retención (incluida la retención para *abuse monitoring* del proveedor).
  Durante el desarrollo, solo datos sintéticos o des-identificados; nunca datos reales de pacientes.
- Credenciales por variables de entorno, User Secrets o Key Vault; nunca en el repositorio, la imagen, la base ni los logs.
- **Caché de deduplicación/costo** (no es idempotencia fuerte): en memoria, ~10 minutos, con clave (usuario, atención,
  huella, versión de prompt, proveedor, modelo). Limitaciones explícitas: es local al proceso, se pierde al reiniciar y
  varias instancias pueden llamar al proveedor por separado; aceptable para un resumen de solo lectura. Una repetición
  exacta del resultado requeriría persistirlo (fuera de alcance).
- Sin `clientRequestId` ni recibo de idempotencia en el PR 1: la operación no escribe nada clínico y el Fake no tiene
  costo; repetirla genera de nuevo.

## Consecuencias

- La vertical completa (contrato, minimización, validación, auditoría, límites y UI) funciona y se prueba sin proveedor
  real ni claves; el PR 2 solo añade una implementación de `IClinicalAiService`.
- Sin cambios de Domain ni migraciones.
- La salida nunca es un dato del expediente: siempre requiere revisión profesional y es una instantánea.
- La app del paciente sigue con su chat **simulado** y educativo (sin backend) y no usa este endpoint; el paramédico no
  tiene IA en esta vertical.
- El PR #23 (observabilidad) también modifica `Web.BFF/Program.cs`: puede producir un conflicto trivial con el orden
  `UseAuthentication` → `UseRateLimiter` y el registro de `AddClinicalAi`; al resolverlo se conserva ese orden.
- Seguimientos: proveedor real (PR 2); health check del proveedor (tras el PR #23, que además debe verificar que su
  middleware de observabilidad no registre cuerpos); métricas agregadas (peticiones, fallos, latencia, tokens) sin
  contenido; resumen de traspaso prehospitalario (P2); explicación educativa de un concepto documentado como operación
  separada (P2).
