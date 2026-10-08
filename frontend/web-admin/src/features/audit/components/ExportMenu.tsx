import { useState } from 'react';
import { Download, FileText } from 'lucide-react';
import type { AuditExportFormat } from '../../../types';
import { Button, Modal } from '../../../components/shared';
import { formatNumber } from '../../../utils/formatters';

export interface ExportMenuProps {
  count: number;
  disabled?: boolean;
  disabledReason?: string;
  onExport: (format: AuditExportFormat) => Promise<boolean>;
  mobile?: boolean;
}

/** Botones "Exportar CSV" / "Exportar PDF" de los eventos filtrados (hoja inferior en móvil). */
export function ExportMenu({ count, disabled = false, disabledReason, onExport, mobile = false }: ExportMenuProps) {
  const [busy, setBusy] = useState<AuditExportFormat | null>(null);
  const [sheetOpen, setSheetOpen] = useState(false);

  const run = async (format: AuditExportFormat) => {
    if (busy) return;
    setBusy(format);
    try {
      if (await onExport(format)) setSheetOpen(false);
    } finally {
      setBusy(null);
    }
  };

  const csvButton = (full: boolean) => (
    <Button
      variant="secondary"
      fullWidth={full}
      leftIcon={<Download size={16} aria-hidden="true" />}
      loading={busy === 'csv'}
      loadingText="Exportando…"
      disabled={disabled || (busy !== null && busy !== 'csv')}
      title={disabled ? disabledReason : undefined}
      onClick={() => void run('csv')}
    >
      Exportar CSV
    </Button>
  );
  const pdfButton = (full: boolean) => (
    <Button
      variant="primary"
      fullWidth={full}
      leftIcon={<FileText size={16} aria-hidden="true" />}
      loading={busy === 'pdf'}
      loadingText="Generando…"
      disabled={disabled || (busy !== null && busy !== 'pdf')}
      title={disabled ? disabledReason : undefined}
      onClick={() => void run('pdf')}
    >
      Exportar PDF
    </Button>
  );

  if (!mobile) {
    return (
      <div className="flex flex-wrap items-center gap-2">
        {csvButton(false)}
        {pdfButton(false)}
      </div>
    );
  }

  return (
    <>
      <Button
        variant="secondary"
        size="sm"
        leftIcon={<Download size={16} aria-hidden="true" />}
        disabled={disabled}
        title={disabled ? disabledReason : undefined}
        aria-haspopup="dialog"
        onClick={() => setSheetOpen(true)}
      >
        Exportar
      </Button>
      <Modal
        open={sheetOpen}
        onClose={() => setSheetOpen(false)}
        placement="bottom"
        dismissible={busy === null}
        title={`Exportar ${formatNumber(count)} eventos`}
        description="Con los filtros aplicados. La exportación queda registrada en auditoría."
      >
        <div className="flex flex-col gap-2.5">
          {csvButton(true)}
          {pdfButton(true)}
          <Button variant="ghost" fullWidth disabled={busy !== null} onClick={() => setSheetOpen(false)}>
            Cancelar
          </Button>
        </div>
      </Modal>
    </>
  );
}
