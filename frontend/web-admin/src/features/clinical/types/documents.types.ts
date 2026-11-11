export type ClinicalDocumentType = 'ClinicalNote' | 'LabResult' | 'Imaging' | 'Referral' | 'Consent' | 'IdentityDocument' | 'Other';

/** Documento de la atención (metadatos): nunca la ruta de almacenamiento ni el checksum. */
export interface ClinicalDocument {
    id: string;
    encounterId: string;
    type: ClinicalDocumentType;
    fileName: string;
    contentType: string;
    sizeBytes: number;
    uploadedAtUtc: string | null;
}

export const DOCUMENT_TYPE_LABELS: Record<ClinicalDocumentType, string> = {
    LabResult: 'Resultado de laboratorio',
    Imaging: 'Imagen diagnóstica',
    Referral: 'Referimiento',
    Consent: 'Consentimiento',
    ClinicalNote: 'Nota clínica',
    IdentityDocument: 'Documento de identidad',
    Other: 'Otro',
};

/** Lo que el servidor acepta (PDF, PNG, JPEG; 10 MB): solo una ayuda, el servidor valida el contenido real. */
export const DOCUMENT_ACCEPT = 'application/pdf,image/png,image/jpeg';
export const DOCUMENT_MAX_BYTES = 10 * 1024 * 1024;

export function formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
