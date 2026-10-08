import { useEffect, useRef, useState, type DragEvent } from 'react';
import { Download, FileSpreadsheet, Upload } from 'lucide-react';
import type { CsvImportPreview } from '../../../types';
import { Button, FormError, LoadingSpinner, Modal } from '../../../components/shared';
import { catalogsService, errorMessage } from '../../../services';
import { cn } from '../../../utils/cn';
import { downloadFile, toCsv } from '../../../utils/csv';

export interface ImportCsvModalProps {
  open: boolean;
  onClose: () => void;
  onImported: (count: number, skipped: number) => void;
}

const MAX_ROWS = 5000;

/** Importación de diagnósticos CIE-10 desde CSV: valida en seco, muestra errores por fila y confirma. */
export function ImportCsvModal({ open, onClose, onImported }: ImportCsvModalProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [preview, setPreview] = useState<CsvImportPreview | null>(null);
  const [validating, setValidating] = useState(false);
  const [importing, setImporting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);

  useEffect(() => {
    if (open) {
      setPreview(null);
      setError(null);
    }
  }, [open]);

  const validate = async (file: File | undefined) => {
    if (!file) return;
    if (!/\.csv$/i.test(file.name)) {
      setError('Selecciona un archivo CSV');
      return;
    }
    setValidating(true);
    setError(null);
    setPreview(null);
    try {
      const p = await catalogsService.previewDiagnosesImport(file);
      if (p.validas.length + p.errores.length > MAX_ROWS)
        throw new Error(`El archivo supera el máximo de ${MAX_ROWS.toLocaleString('es')} filas.`);
      setPreview(p);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setValidating(false);
    }
  };

  const run = async () => {
    if (!preview) return;
    setImporting(true);
    try {
      const n = await catalogsService.commitDiagnosesImport(preview);
      onImported(n, preview.errores.length);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setImporting(false);
    }
  };

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    setDragging(false);
    void validate(e.dataTransfer.files[0]);
  };

  const template = () =>
    downloadFile(
      'plantilla_diagnosticos.csv',
      toCsv([
        ['codigo', 'descripcion', 'categoria'],
        ['J18.9', 'Neumonía, no especificada', 'Respiratorio'],
      ]),
    );

  return (
    <Modal
      open={open}
      onClose={onClose}
      dismissible={!importing}
      title="Importar diagnósticos (CSV)"
      description="Columnas: codigo, descripcion, categoria. Máx. 5 000 filas."
      footer={
        <>
          <Button variant="ghost" leftIcon={<Download size={15} aria-hidden="true" />} onClick={template} className="mr-auto">
            Descargar plantilla CSV
          </Button>
          <Button variant="secondary" onClick={onClose} disabled={importing}>
            Cancelar
          </Button>
          <Button
            onClick={run}
            disabled={!preview || preview.validas.length === 0 || validating}
            loading={importing}
            loadingText="Importando…"
          >
            {preview ? `Importar ${preview.validas.length} ${preview.validas.length === 1 ? 'fila' : 'filas'}` : 'Importar'}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <FormError message={error} />
        <div
          onDragOver={(e) => {
            e.preventDefault();
            setDragging(true);
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={onDrop}
          className={cn(
            'flex flex-col items-center gap-2 rounded-xl border-2 border-dashed px-4 py-8 text-center',
            dragging ? 'border-primary-600 bg-primary-50' : 'border-line',
          )}
        >
          {preview ? (
            <FileSpreadsheet size={26} className="text-primary-600" aria-hidden="true" />
          ) : (
            <Upload size={26} className="text-ink-muted" aria-hidden="true" />
          )}
          <p className="text-sm text-ink-3">{preview ? preview.archivo : 'Arrastra el archivo o haz clic para elegirlo (.csv, UTF-8)'}</p>
          <Button variant="secondary" size="sm" onClick={() => inputRef.current?.click()}>
            {preview ? 'Elegir otro archivo' : 'Elegir archivo'}
          </Button>
          <input
            ref={inputRef}
            type="file"
            accept=".csv,text/csv"
            className="sr-only"
            aria-label="Archivo CSV de diagnósticos"
            onChange={(e) => {
              void validate(e.target.files?.[0]);
              e.target.value = '';
            }}
          />
        </div>
        <div aria-live="polite">
          {validating && <LoadingSpinner label="Validando filas…" />}
          {preview && (
            <div className="flex flex-col gap-2 text-[13px]">
              <p>
                <strong className="text-success-600">{preview.validas.length} filas válidas</strong>
                {preview.errores.length > 0 && (
                  <>
                    {' · '}
                    <strong className="text-danger-text">{preview.errores.length} con errores</strong> (se omitirán)
                  </>
                )}
              </p>
              {preview.errores.length > 0 && (
                <ul className="max-h-40 overflow-y-auto rounded-lg bg-danger-50 px-3 py-2 text-xs text-danger-text">
                  {preview.errores.map((e) => (
                    <li key={e.fila}>
                      Fila {e.fila}: {e.mensaje}
                    </li>
                  ))}
                </ul>
              )}
              {preview.validas.length === 0 && <p className="text-ink-muted">No hay filas válidas para importar.</p>}
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
}
