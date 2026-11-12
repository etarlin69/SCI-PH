import { apiBlob, apiRequest } from '../../../api';
import type { ClinicalDocument, ClinicalDocumentType } from '../types/documents.types';

// Documentos de la atención (Web BFF real). El BFF entrega el archivo al Document Service; nunca hay rutas de
// almacenamiento en el navegador. Rutas: docs/HOSPITAL_CONTRATOS.md.
const BASE = '/api/web/encounters';
const id = (value: string) => encodeURIComponent(value);

export const documentsApi = {
    list: (encounterId: string, signal?: AbortSignal) => apiRequest<ClinicalDocument[]>(`${BASE}/${id(encounterId)}/documents`, { signal }),

    /** Multipart con exactamente file, type y clientRequestId (uno por intento, conservado en los reintentos). */
    upload: (encounterId: string, file: File, type: ClinicalDocumentType, clientRequestId: string) => {
        const form = new FormData();
        form.append('file', file, file.name);
        form.append('type', type);
        form.append('clientRequestId', clientRequestId);
        return apiRequest<ClinicalDocument>(`${BASE}/${id(encounterId)}/documents`, { method: 'POST', body: form });
    },

    /** El contenido como Blob (con la sesión en memoria); el llamador lo guarda con una URL de objeto temporal. */
    download: (encounterId: string, documentId: string) => apiBlob(`${BASE}/${id(encounterId)}/documents/${id(documentId)}/content`),
};
