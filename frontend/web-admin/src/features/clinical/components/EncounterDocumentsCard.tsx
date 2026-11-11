import { useRef, useState, type FormEvent } from 'react';
import { Permissions, useAuth } from '../../../auth';
import { Button, Card, EmptyState, Select, useToast } from '../../../components/shared';
import { describeError } from '../api';
import { documentsApi } from '../api/documentsApi';
import { useClinicalQuery } from '../hooks/useClinicalQuery';
import type { ClinicalEncounterSummary } from '../types/clinical.types';
import {
    DOCUMENT_ACCEPT,
    DOCUMENT_MAX_BYTES,
    DOCUMENT_TYPE_LABELS,
    formatFileSize,
    type ClinicalDocument,
    type ClinicalDocumentType,
} from '../types/documents.types';
import { acceptsClinicalRecords, formatDateTimeUtc } from '../utils/clinicalLabels';
import { QueryView } from './QueryView';

/**
 * Documentos de la atención: listar y descargar (Documents.Read) y adjuntar PDF/PNG/JPEG de hasta 10 MB (Documents.Upload,
 * con la atención abierta). El servidor valida el contenido real; aquí solo se evita enviar lo que seguro rechazará. Una
 * subida conserva su clientRequestId mientras se reintenta el MISMO archivo.
 */
export function EncounterDocumentsCard({ encounter }: { encounter: ClinicalEncounterSummary }) {
    const { can } = useAuth();
    const toast = useToast();
    const canUpload = can(Permissions.Documents.Upload) && acceptsClinicalRecords(encounter.status);
    const { state, reload } = useClinicalQuery(`documents:${encounter.id}`, (signal) => documentsApi.list(encounter.id, signal));
    const [type, setType] = useState<ClinicalDocumentType | ''>('');
    const [file, setFile] = useState<File | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [saving, setSaving] = useState(false);
    const [downloading, setDownloading] = useState<string | null>(null);
    const request = useRef<{ file: File; type: ClinicalDocumentType; id: string } | null>(null);
    const input = useRef<HTMLInputElement>(null);

    const choose = (selected: File | null) => {
        setError(null);
        if (selected && selected.size > DOCUMENT_MAX_BYTES) {
            setFile(null);
            setError('El archivo supera los 10 MB.');
            return;
        }
        setFile(selected);
    };

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        if (saving || !file || !type) return;
        if (request.current?.file !== file || request.current.type !== type) request.current = { file, type, id: crypto.randomUUID() };
        setSaving(true);
        setError(null);
        try {
            await documentsApi.upload(encounter.id, file, type, request.current.id);
            toast({ title: 'Documento adjuntado', tone: 'success' });
            request.current = null;
            setFile(null);
            setType('');
            if (input.current) input.current.value = '';
            reload();
        } catch (failure) {
            // El error se muestra junto al formulario; el mismo archivo se reintenta con la misma clave.
            setError(describeError(failure));
        } finally {
            setSaving(false);
        }
    };

    const download = async (document: ClinicalDocument) => {
        setDownloading(document.id);
        try {
            const blob = await documentsApi.download(encounter.id, document.id);
            const url = URL.createObjectURL(blob);
            const link = window.document.createElement('a');
            link.href = url;
            link.download = document.fileName;
            link.rel = 'noopener';
            link.click();
            URL.revokeObjectURL(url);
        } catch (failure) {
            toast({ title: 'No se pudo descargar el documento.', description: describeError(failure), tone: 'error' });
        } finally {
            setDownloading(null);
        }
    };

    return (
        <Card title="Documentos">
            <QueryView
                state={state}
                onRetry={reload}
                errorTitle="No se pudieron cargar los documentos"
                isEmpty={(list) => list.length === 0}
                empty={<EmptyState title="Sin documentos adjuntos" />}
            >
                {(list) => (
                    <ul className="m-0 flex list-none flex-col gap-2 p-0">
                        {list.map((d) => (
                            <li key={d.id} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-line-soft p-3">
                                <span className="flex min-w-0 flex-col gap-0.5">
                                    <span className="truncate text-sm font-semibold text-ink" title={d.fileName}>
                                        {d.fileName}
                                    </span>
                                    <span className="text-xs text-ink-muted">
                                        {DOCUMENT_TYPE_LABELS[d.type] ?? d.type} · {formatFileSize(d.sizeBytes)}
                                        {d.uploadedAtUtc && <> · {formatDateTimeUtc(d.uploadedAtUtc)}</>}
                                    </span>
                                </span>
                                <Button
                                    size="sm"
                                    variant="secondary"
                                    onClick={() => download(d)}
                                    loading={downloading === d.id}
                                    loadingText="Descargando…"
                                    aria-label={`Descargar ${d.fileName}`}
                                >
                                    Descargar
                                </Button>
                            </li>
                        ))}
                    </ul>
                )}
            </QueryView>

            {canUpload && (
                <form onSubmit={submit} className="mt-5 flex flex-col gap-3 border-t border-line-soft pt-4">
                    <h3 className="m-0 text-sm font-semibold text-ink">Adjuntar documento</h3>
                    <div className="grid gap-3 sm:grid-cols-2">
                        <Select
                            label="Tipo de documento"
                            value={type}
                            onChange={(e) => setType(e.target.value as ClinicalDocumentType | '')}
                            options={(Object.keys(DOCUMENT_TYPE_LABELS) as ClinicalDocumentType[]).map((t) => ({ value: t, label: DOCUMENT_TYPE_LABELS[t] }))}
                            placeholder="Selecciona…"
                            required
                        />
                        <label className="flex flex-col gap-1 text-sm text-ink-2">
                            <span className="font-medium">Archivo (PDF, PNG o JPEG; máx. 10 MB)</span>
                            <input
                                ref={input}
                                type="file"
                                accept={DOCUMENT_ACCEPT}
                                onChange={(e) => choose(e.target.files?.[0] ?? null)}
                                className="text-sm"
                            />
                        </label>
                    </div>
                    {error && (
                        <p role="alert" className="m-0 text-sm text-danger-text">
                            {error}
                        </p>
                    )}
                    <div>
                        <Button type="submit" loading={saving} loadingText="Subiendo…" disabled={!file || !type}>
                            Adjuntar
                        </Button>
                    </div>
                </form>
            )}
        </Card>
    );
}
