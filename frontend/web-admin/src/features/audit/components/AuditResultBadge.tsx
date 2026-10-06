import { Check, X } from 'lucide-react';
import type { AuditResult } from '../../../types';
import { AUDIT_RESULT_TONE, Badge } from '../../../components/shared';

export interface AuditResultBadgeProps {
  result: AuditResult;
  error?: string;
}

/** Etiqueta de resultado (Éxito / Error) con icono, y el motivo del fallo si lo hay. */
export function AuditResultBadge({ result, error }: AuditResultBadgeProps) {
  const ok = result === 'Éxito';
  return (
    <span className="inline-flex min-w-0 flex-col items-start gap-0.5">
      <Badge tone={AUDIT_RESULT_TONE[result]} dot={false}>
        {ok ? <Check size={12} aria-hidden="true" /> : <X size={12} aria-hidden="true" />}
        {result}
      </Badge>
      {!ok && error && (
        <span className="max-w-[220px] truncate text-xs text-danger-text" title={error}>
          {error}
        </span>
      )}
    </span>
  );
}
