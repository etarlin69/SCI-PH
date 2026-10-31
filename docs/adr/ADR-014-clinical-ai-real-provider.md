# ADR-014: Proveedor real de IA clínica y frontera de datos externos

- **Estado:** Aceptado. Implementado en `feature/clinical-ai-real-provider` (PR 2/2 de [ADR-013](ADR-013-clinical-ai-assistant.md)).
- **Fecha:** 2026-10-31
- **Relacionado:** [ADR-013](ADR-013-clinical-ai-assistant.md), [ADR-001](ADR-001-onion-architecture.md),
  [ADR-006](ADR-006-identity-security.md), [contratos del Web BFF](../api/web-bff-contracts.md#web-hospital--resumen-asistido-por-ia-implementado)

> **Este ADR no autoriza el envío de PHI real.** En demo y en el ámbito académico, la IA externa recibe **solo datos
> sintéticos**. Usar datos reales de pacientes en producción exige antes la revisión institucional, legal, de privacidad y
> de cumplimiento descrita en la decisión 4. SCI-PH **no** afirma ser conforme con HIPAA ni tener un BAA, *zero data
> retention* o *modified abuse monitoring* configurados: nada de eso está verificado ni configurado por este PR.

## Contexto

El PR 1 (ADR-013) dejó la vertical completa con un proveedor Fake en proceso y `AzureOpenAI` reservado. Este PR añade el
primer proveedor que **envía la entrada minimizada fuera de SCI-PH**. Eso cambia tres cosas: la entrada sale del sistema
(auditoría y gobierno de datos), cada llamada cuesta dinero (reintentos, deduplicación, métricas de tokens) y el proveedor
tiene sus propios fallos (filtros de contenido, negativas, cuotas, límites de tasa).

## Decisiones

### 1. Azure OpenAI (Microsoft Foundry) como proveedor; OpenAI directo como plan B no implementado

- **Por qué Azure OpenAI** (no por usar ya Azure Blob): según su documentación de privacidad, los prompts y respuestas no
  se usan para entrenar ni están disponibles para OpenAI, los modelos no guardan estado, el procesamiento puede acotarse a
  una zona de datos (Data Zone US/EU) y lo almacenado queda en la geografía del recurso; admite Microsoft Entra ID con
  identidad administrada (sin secretos) y el mismo SDK oficial de .NET que OpenAI. El gobierno contractual futuro pasa por
  el acuerdo de Microsoft.
- **OpenAI API directa:** comparable (sin entrenamiento por defecto, residencia US/EU, BAA posible), pero añade otra
  relación contractual y no tiene identidad administrada. Queda como plan B, **no implementado**.
- **Modelo local (Ollama/llama.cpp):** referencia futura detrás de la misma interfaz (los datos no saldrían de SCI-PH).

### 2. Modelo y despliegue

- `gpt-5.6-luna`, versión `2026-07-09` (GA; retiro previsto 2028-01-11), en un despliegue **Data Zone Standard US**.
  `ModelVersion` configurado: `gpt-5.6-luna-2026-07-09`; es lo que ven la respuesta, la auditoría y las métricas.
- El **nombre del despliegue** es configuración interna: nunca llega a la API, la UI, la auditoría ni los logs.
- Alternativa por configuración (sin código): `gpt-5.4-mini` (retiro 2027-09-21, ~3,7× más caro). Evitar `gpt-5-mini`
  (retiro 2027-02-09) y modelos deprecados.

### 3. Frontera de abstracción

- Application solo conoce `IClinicalAiService`; el SDK (`OpenAI` 2.14.0) y `Azure.Identity` 1.21.0 están **solo en
  Infrastructure** (`AzureOpenAiClinicalAiService`). Sin `Azure.AI.OpenAI` ni `Microsoft.Extensions.AI`: Application no se
  convierte en un `IChatClient` ni construye prompts.
- Tres adiciones opcionales y compatibles a la abstracción (el contrato HTTP no cambia):
  - `ClinicalAiUsage? Usage` en `EncounterSummaryDraft` (tokens de entrada y salida; razonamiento si se informa);
  - `bool SendsDataExternally` (por defecto `false`; Fake y Disabled `false`, Azure `true`);
  - `ClinicalAiFailure` (categoría) en `ClinicalAiProviderException`, más `BeforeTransmission` (la entrada no salió).
- Restaurar el SDK obligó a subir en Infrastructure `Microsoft.Extensions.Configuration.Abstractions` y
  `Microsoft.Extensions.DependencyInjection.Abstractions` de 10.0.3 a 10.0.9 (NU1605: `System.ClientModel` 1.15 los exige).
  Ningún otro paquete cambió de versión directa; las vulnerabilidades de paquetes conocidas de la solución son las mismas
  que antes.

### 4. Frontera de datos externos: `ClinicalAi:AllowExternalClinicalData`

- Un proveedor con `SendsDataExternally = true` **no arranca** si `ClinicalAi:AllowExternalClinicalData` no es `true`
  (validación al inicio y segunda comprobación al construir el proveedor). Elegir `Provider = AzureOpenAI` no basta: el
  permiso es una decisión deliberada y separada.
- **Demo/académico:** solo datos sintéticos o des-identificados, también en el E2E real.
- **Producción con datos reales:** antes de habilitar el permiso, revisión institucional, legal, de privacidad y de
  cumplimiento: acuerdo de tratamiento de datos aplicable (DPA/BAA o equivalente), región y configuración del despliegue,
  retención del *abuse monitoring* del proveedor (o su modificación aprobada, que no es autoservicio) y normativa local.
  El texto libre (notas) puede contener identificadores aunque la entrada esté minimizada.
- La entrada enviada es solo la del PR 1 (`EncounterAiInputBuilder`): sin identidad, sin ids, sin fechas absolutas.

### 5. Autenticación

- **Production:** `AuthenticationMode = ManagedIdentity` (Microsoft Entra ID, identidad del sistema o asignada por el usuario;
  rol *Cognitive Services OpenAI User*). `ApiKey` o `DefaultAzureCredential` en Production, o una clave configurada junto a
  Entra ID, impiden arrancar.
- **Development:** `ApiKey` (User Secrets o variable de entorno) o `DefaultAzureCredential` (`az login`).
- `TokenScope` configurable (por defecto `https://cognitiveservices.azure.com/.default`; la documentación de Microsoft usa
  también `https://ai.azure.com/.default` en algunas guías: verificarlo con el E2E real).
- Nunca hay secretos en `appsettings*.json`, el repositorio, la imagen, los logs ni la auditoría; los mensajes de validación
  no repiten valores configurados.
- **Sin llamada al arrancar:** construir el cliente no contacta a nadie (ni token ni petición); la disponibilidad se conoce
  por petición. Sin health check del proveedor en este PR (seguimiento tras el PR #23 de observabilidad).

### 6. Petición: Chat Completions sin estado y salida estructurada estricta

- **Chat Completions** contra `https://<recurso>.openai.azure.com/openai/v1/`, `store: false`, sin historial, sin
  herramientas. **No** Responses API (guarda estado). Cada llamada es independiente.
- Solo dos mensajes: el mensaje de sistema fijo de `encounter-summary.v1` y `{"encounterData": …}`.
- Parámetros: `reasoning_effort = none`, `max_completion_tokens = 6000` (incluye razonamiento), `response_format =
  json_schema` estricto. **No** se envían `temperature`, `top_p` ni `max_tokens` (los modelos de razonamiento no los
  admiten). El SDK marca `reasoning_effort` y el constructor con Entra ID como evaluación (`OPENAI001`): se suprime solo en
  esas líneas, porque son los parámetros documentados.

### 7. Schema estricto: solo estructura; los límites siguen en el validador

- El modo estricto de Structured Outputs admite un subconjunto de JSON Schema. El `ResponseSchema` V1 usaba `maxLength`,
  `maxItems` y `type: ["object", "null"]` con `$ref`. Se corrige su **representación**:
  - todos los campos `required`, `additionalProperties: false` en cada objeto, raíz objeto normal (sin `anyOf`);
  - secciones nulables como `anyOf: [{ "$ref": "#/$defs/section" }, { "type": "null" }]`;
  - **sin** `minLength`, `maxLength`, `pattern`, `format`, `minimum`, `maximum`, `multipleOf`, `minItems`, `maxItems`,
    `uniqueItems`, `patternProperties`, `unevaluatedProperties`, `propertyNames`, `minProperties`, `maxProperties`,
    `unevaluatedItems`, `contains`, `minContains` ni `maxContains`. Un test recorre el schema y falla si aparece alguno.
- Longitudes, cantidades y límites por sección siguen siendo responsabilidad de `EncounterAiOutputValidator` (sin cambios)
  y del texto del prompt (regla 7). El schema del proveedor **nunca** reemplaza al validador.
- **La versión sigue siendo `encounter-summary.v1`:** no cambian el texto del prompt, su significado ni el contrato clínico;
  solo la forma de expresar el schema para el primer proveedor real.

### 8. Respuesta, negativas y filtros

- Respuesta → `ParseDraft` → `EncounterAiOutputValidator` → resumen. JSON válido pero semánticamente inválido (p. ej. una
  fuente inexistente) es `InvalidStructuredOutput`.
- Una **negativa** (`refusal`) nunca se parsea: `ContentFiltered`. Un `finish_reason = content_filter` o un 400 de filtro
  sobre la entrada: `ContentFiltered`. Notas clínicas legítimas (trauma, sobredosis, autolesión, violencia) pueden activar
  filtros: la categoría no implica entrada maliciosa. `finish_reason = length`: salida inválida.
- El texto clínico libre es **dato no confiable**: el mensaje de sistema lo declara y la salida estructurada más el
  validador siguen siendo obligatorios.

### 9. Fallos, reintentos y tiempo

| Causa | Categoría (`ClinicalAiFailure`) | Reintento |
|---|---|---|
| tiempo agotado | `Timeout` | ninguno |
| 429 por tasa (RPM/TPM) | `RateLimitedByProvider` | ninguno |
| 403/429 por cuota | `QuotaExceeded` | ninguno |
| 401 / 403 / sin token Entra ID | `AuthenticationFailed` | ninguno |
| filtro (400 o `finish_reason`), negativa | `ContentFiltered` | ninguno |
| JSON ilegible, cortado o rechazado por el validador | `InvalidStructuredOutput` | **1 regeneración** (Application), solo si pasó menos de la mitad del tiempo |
| red, 408, 5xx | `ProviderUnavailable` | **1 reintento** (adaptador), solo si pasó menos de la mitad del tiempo |
| 400 de petición/schema, 404 de despliegue, entrada sobre el tope | `ConfigurationError` | ninguno |

- Reintentos del SDK desactivados (`ClientRetryPolicy(0)`); nunca un bucle. Cada reintento cuesta dinero.
- Para el cliente, todo es el mismo **503** genérico; nuestro limitador sigue devolviendo **429**. Nunca se propagan
  400/401/403/404/429/5xx del proveedor, su mensaje, cabeceras, request id, despliegue, prompt ni contenido; la excepción
  del SDK no se adjunta (su mensaje contiene la respuesta del proveedor). La auditoría conserva el `failure` del PR 1 y
  añade la `category`.
- Tiempo: `ClinicalAi:TimeoutSeconds` (30 por defecto) acota la operación entera, reintento y regeneración incluidos; el
  timeout de red del SDK queda por encima. La cancelación del cliente cancela la petición HTTP al proveedor.
- Entrada: la serialización del PR 1 está acotada (~60 000 caracteres, ~15–20 mil tokens); por encima de 80 000
  caracteres el adaptador no envía nada (`ConfigurationError`, antes de transmitir).

### 10. Caché en memoria (deduplicación de costo, no idempotencia)

- `EncounterAiSummaryCache` en Application (`MemoryCache` propio): clave usuario + atención + huella de la entrada +
  versión de prompt + proveedor + modelo; 10 minutos absolutos; `SizeLimit` 500. Solo para proveedores externos (el Fake
  responde siempre, como en el PR 1).
- Solo entra un resumen **validado**; nunca un error, timeout, negativa, filtro ni salida inválida.
- Un acierto devuelve el mismo resumen (mismo `generatedAtUtc`), sin llamada ni tokens nuevos, auditado como `Read`.
- Privacidad: solo memoria del proceso (sin base de datos, Redis, Blob, disco ni serialización), nunca en logs, particionada
  por usuario. Un registro nuevo cambia la huella: fallo de caché. Se pierde al reiniciar y cada instancia tiene la suya.

### 11. Auditoría `Export`

- `Action = Export` cuando la entrada se envió (o pudo enviarse) a un proveedor externo, con éxito o con fallo.
  `Read`: acierto de caché, Disabled, rechazo antes de transmitir y el Fake en proceso. Sin cambio de Domain (`Export`
  ya existía).
- `ChangesJson` añade solo metadatos: `category`, `providerCalls`, `cacheHit` y `usage` (tokens). Nunca prompt,
  respuesta, contenido clínico, despliegue ni claves.

### 12. Métricas: Meter `SCI-PH.ClinicalAi`

- `ai_requests_total`, `ai_failures_total`, `ai_request_duration` (s), `ai_input_tokens`, `ai_output_tokens`,
  `ai_cache_hits_total`. Etiquetas de baja cardinalidad: `provider`, `model` (versión configurada), `operation`,
  `outcome` (resultado o categoría). Nunca usuario, atención, paciente, huella ni contenido.

### 13. Pruebas

- **CI sin Azure:** transporte HTTP simulado del SDK (`ScriptedAzureOpenAi`) que valida la petición real del adaptador
  (endpoint, despliegue, schema estricto y sus palabras clave, `required`, `additionalProperties`, nulables, sin
  `temperature`/`top_p`/`max_tokens`, `store: false`, razonamiento `none`, 6000 tokens, solo la entrada minimizada) y
  cada fallo, reintento y regeneración; también de extremo a extremo por el Web BFF con PostgreSQL. El Fake sigue siendo el
  proveedor de CI y por defecto; `dotnet test`, el frontend y la CI no necesitan credenciales.
- **E2E real opt-in:** `SCIPH_AI_REAL_E2E=1` con `SCIPH_AI_AZURE_ENDPOINT`, `SCIPH_AI_AZURE_DEPLOYMENT`,
  `SCIPH_AI_AZURE_AUTH` (`DefaultAzureCredential` o `ApiKey`) y `SCIPH_AI_AZURE_API_KEY`; sin ellos se **omite** (nunca
  falla la CI). Solo datos sintéticos; no se guardan respuestas reales como artefactos. Los filtros y negativas no se
  provocan a propósito.

## Configuración

```jsonc
"ClinicalAi": {
  "Provider": "AzureOpenAI",
  "AllowExternalClinicalData": false,     // true solo deliberadamente (decisión 4)
  "TimeoutSeconds": 30,                   // toda la operación (1–120)
  "AzureOpenAI": {
    "Endpoint": "https://<recurso>.openai.azure.com/",
    "Deployment": "<despliegue Data Zone>",          // interno
    "ModelVersion": "gpt-5.6-luna-2026-07-09",
    "AuthenticationMode": "ManagedIdentity",         // Development: ApiKey o DefaultAzureCredential
    "ManagedIdentityClientId": null,                 // identidad asignada por el usuario (opcional)
    "TokenScope": "https://cognitiveservices.azure.com/.default",
    "MaxCompletionTokens": 6000                       // 1000–32000
    // "ApiKey": solo User Secrets / variable de entorno, solo Development
  }
}
```

## Coste estimado (no es una cotización)

Precios minoristas de Azure para `gpt-5.6-luna` (USD por 1M de tokens, eastus2, consultados el 2026-10-30): Global 0,20
entrada / 1,20 salida; Data Zone 0,22 / 1,32. Un resumen típico de 6 000 tokens de entrada y 1 000 de salida cuesta
≈ 0,0026 USD en Data Zone: ≈ 0,26 USD/día con 100 resúmenes y ≈ 2,64 USD/día con 1 000 (peor caso 10K/1,5K: ≈ 4,18
USD/día). La caché reduce las repeticiones.

## Consecuencias

- El contrato HTTP y la UI del PR 1 no cambian; la abstracción de Application gana tres miembros opcionales.
- Sin cambios de Domain ni migraciones.
- Activar el proveedor real es una decisión de configuración en dos pasos (proveedor + permiso) que deja rastro (`Export`).
- Limitaciones: la caché es local al proceso; la retención del *abuse monitoring* de Azure no figura con duración en su
  página oficial actual; la calidad en español debe evaluarse con el E2E real antes de fijar el modelo; los filtros por
  defecto pueden bloquear notas clínicas legítimas (ajustarlos o solicitar su modificación es un seguimiento).
- Seguimientos: health check del proveedor (tras el PR #23); configuración de filtros adecuada al dominio clínico;
  evaluación luna vs 5.4-mini en español; proveedor OpenAI directo y local (Ollama) detrás de la misma interfaz.

## Fuentes (consultadas el 2026-10-30)

- Microsoft Learn — [Data, privacy, and security for models sold by Azure](https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/openai/data-privacy)
- Microsoft Learn — [Abuse monitoring](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/abuse-monitoring)
- Microsoft Learn — [Model retirement schedule](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/model-retirement-schedule)
- Microsoft Learn — [Region availability](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability)
- Microsoft Learn — [Reasoning models](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning)
- Microsoft Learn — [Content filtering](https://learn.microsoft.com/azure/ai-foundry/openai/concepts/content-filter)
- Microsoft Learn — [Supported programming languages (OpenAI .NET + Azure.Identity, v1)](https://learn.microsoft.com/azure/foundry/openai/supported-languages)
- [Azure Retail Prices API](https://prices.azure.com/api/retail/prices) (`serviceName eq 'Foundry Models'`)
- OpenAI — [Structured outputs](https://developers.openai.com/api/docs/guides/structured-outputs), [Data controls](https://developers.openai.com/api/docs/guides/your-data)
- NuGet — `OpenAI` 2.14.0, `Azure.Identity` 1.21.0
