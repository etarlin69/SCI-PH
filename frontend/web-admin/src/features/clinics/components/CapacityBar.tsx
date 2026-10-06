import { cn } from '../../../utils/cn';

export interface CapacityBarProps {
  ocupadas: number;
  total: number;
  unidad: string;
  label: string;
  className?: string;
}

/** Ocupación de un servicio: «18 de 24 camas ocupadas · 75%» con barra (verde < 70 %, ámbar < 85 %, rojo ≥ 85 %). */
export function CapacityBar({ ocupadas, total, unidad, label, className }: CapacityBarProps) {
  const pct = total > 0 ? Math.min(100, Math.round((ocupadas / total) * 100)) : 0;
  const color = pct >= 85 ? 'bg-danger-600' : pct >= 70 ? 'bg-warning-500' : 'bg-success-600';
  const text = `${ocupadas} de ${total} ${unidad} ocupadas · ${pct}%`;
  return (
    <div className={cn('flex min-w-0 flex-col gap-1.5', className)}>
      <span className="tabular text-xs text-ink-3">{text}</span>
      <span
        role="meter"
        aria-label={`Ocupación de ${label}`}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={pct}
        aria-valuetext={text}
        className="block h-1.5 w-full overflow-hidden rounded-full bg-line-soft"
      >
        <span className={cn('block h-full rounded-full', color)} style={{ width: `${pct}%` }} />
      </span>
    </div>
  );
}
