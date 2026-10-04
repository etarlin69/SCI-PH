import { History } from 'lucide-react';
import type { StaffHistoryEntry } from '../../../types';
import { EmptyState, ErrorState, LoadingSpinner } from '../../../components/shared';
import { cn } from '../../../utils/cn';
import { formatDMY } from '../../../utils/dates';

export interface HistoryTimelineProps {
  entries: StaffHistoryEntry[];
  loading: boolean;
  error: string | null;
  onRetry: () => void;
}

const DOT = { info: 'bg-primary-600', success: 'bg-success-600', warning: 'bg-warning-500', danger: 'bg-danger-600' };

/** Pestaña "Histórico": altas, traslados, cambios de estado y avisos, del más reciente al más antiguo. */
export function HistoryTimeline({ entries, loading, error, onRetry }: HistoryTimelineProps) {
  if (error) return <ErrorState variant="banner" title="No se pudo cargar el histórico" message={error} onRetry={onRetry} />;
  if (loading) return <LoadingSpinner label="Cargando histórico…" />;
  if (entries.length === 0) return <EmptyState icon={History} title="Sin movimientos" />;
  return (
    <ol className="flex flex-col gap-4 border-l border-line pl-5">
      {entries.map((h) => (
        <li key={h.id} className="relative flex flex-col gap-0.5">
          <span
            aria-hidden="true"
            className={cn('absolute -left-[25px] top-1.5 h-2.5 w-2.5 rounded-full ring-4 ring-white', DOT[h.tone])}
          />
          <span className="tabular text-xs text-ink-muted">{formatDMY(h.fecha)}</span>
          <span className="text-sm font-medium text-ink">{h.titulo}</span>
          <span className="text-xs text-ink-muted">{h.detalle}</span>
        </li>
      ))}
    </ol>
  );
}
