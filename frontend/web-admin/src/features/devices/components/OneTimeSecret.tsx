import { useId, useState } from 'react';
import { AlertTriangle, Eye, EyeOff } from 'lucide-react';
import { CopyButton } from '../../../components/shared';
import { cn } from '../../../utils/cn';
import { groupSecret } from '../helpers';

export interface OneTimeSecretProps {
  secret: string;
  expired?: boolean;
}

/** Secreto de un solo uso: oculto por defecto, con mostrar/ocultar, copiar al portapapeles y el aviso de que no volverá a mostrarse. */
export function OneTimeSecret({ secret, expired = false }: OneTimeSecretProps) {
  const [revealed, setRevealed] = useState(false);
  const labelId = useId();
  const grouped = groupSecret(secret);
  const masked = grouped.replace(/[^ ]/g, '•');
  const visible = revealed && !expired;

  return (
    <div className="flex flex-col gap-2">
      <span id={labelId} className="text-[13px] font-semibold text-ink-2">
        Secreto de un solo uso
      </span>
      <div
        aria-labelledby={labelId}
        className={cn(
          'break-all rounded-lg border border-line bg-surface-subtle px-3 py-2.5 font-mono text-[15px] tracking-wide',
          expired ? 'text-ink-disabled line-through' : 'text-ink',
        )}
      >
        {visible ? grouped : <span aria-hidden="true">{masked}</span>}
        {!visible && <span className="sr-only">{expired ? 'Secreto caducado' : 'Secreto oculto'}</span>}
      </div>
      {!expired && (
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            aria-pressed={revealed}
            onClick={() => setRevealed((r) => !r)}
            className="inline-flex h-9 items-center gap-1.5 rounded-md border border-line bg-white px-3 text-[13px] font-medium text-ink-3 hover:bg-surface-hover focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
          >
            {revealed ? <EyeOff size={14} aria-hidden="true" /> : <Eye size={14} aria-hidden="true" />}
            {revealed ? 'Ocultar' : 'Mostrar'}
            <span className="sr-only"> secreto</span>
          </button>
          <CopyButton value={secret} label="Copiar secreto de un solo uso" />
        </div>
      )}
      <p className="flex items-start gap-2 rounded-lg bg-warning-50 px-3 py-2 text-xs leading-snug text-warning-700">
        <AlertTriangle size={14} className="mt-px shrink-0" aria-hidden="true" />
        <span>No volverá a mostrarse. Si se pierde, rota la credencial desde el detalle del dispositivo.</span>
      </p>
    </div>
  );
}
