# ADR-004: Almacenamiento de documentos

- **Estado:** Aceptado
- **Fecha:** 2026-09-27

## Contexto

SCI-PH manejará documentos clínicos (estudios, imágenes, documentos escaneados para OCR, etc.). Guardar archivos binarios dentro de PostgreSQL aumenta el tamaño de la base, encarece los respaldos y empeora el rendimiento.

## Decisión

Separamos **metadata** y **archivo físico**:

| Qué | Dónde |
|---|---|
| Metadata del documento | PostgreSQL, schema `clinical` |
| Archivo físico | Azure Blob Storage |

La metadata incluye, como mínimo previsto:

- Identificador del documento
- Relación con la entidad clínica (por ejemplo, paciente)
- Nombre original, tipo de contenido y tamaño
- Referencia al blob (contenedor y nombre del blob), **no** una URL pública permanente
- Fecha de carga y quién lo cargó
- Hash del contenido, para verificar integridad

Reglas:

- Application define `IStorageService` (y `IDocumentService`); Infrastructure implementa el acceso a Azure Blob.
- Domain y Application no referencian el SDK de Azure.
- Los contenedores no son públicos. El acceso a un archivo se da con URLs temporales de alcance limitado (SAS) o descargando a través del BFF.
- Las credenciales de Azure nunca se guardan en el repositorio.

## Consecuencias

**Positivas**
- La base se mantiene liviana y los respaldos son más rápidos.
- Azure Blob está diseñado para archivos grandes y acceso concurrente.
- El proveedor de almacenamiento se puede cambiar implementando `IStorageService`.

**Negativas**
- La consistencia entre metadata y blob no es transaccional: hay que manejar blobs huérfanos o metadata sin archivo.
- Dependencia de un servicio externo para leer documentos.

**Pendiente**
- Estrategia para desarrollo local (por ejemplo, Azurite) y para limpiar blobs huérfanos.
- Política de retención y borrado.

## Implementación (2026-11)

- `IStorageService` (Application) con tres proveedores en Infrastructure, elegidos por `DocumentStorage:Provider`:
  `AzureBlob` (contenedor privado; también Azurite en local), `FileSystem` (solo desarrollo, rechazado en Production) y
  `Disabled` (por defecto: 503 genérico).
- `ClinicalDocumentService` (`IDocumentService`): documentos de una **atención**; tipo detectado por el contenido real
  (PDF/PNG/JPEG), 10 MB, nombre saneado (solo de visualización), ruta generada por el servidor, SHA-256, idempotencia
  por `clientRequestId`, auditoría de subida y descarga. La descarga pasa por el BFF como adjunto (`nosniff`); no se
  usan URLs SAS.
- Si guardar la metadata falla, el objeto se borra (mejor esfuerzo). Una metadata sin objeto responde 404.

**Sigue pendiente:** documentos del registro del paciente y su visibilidad en la app del paciente (decisión de producto),
barrido periódico de blobs huérfanos, política de retención y borrado, y una prueba E2E contra Azure real (solo validado
con Azurite).
