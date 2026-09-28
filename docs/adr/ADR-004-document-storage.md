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
