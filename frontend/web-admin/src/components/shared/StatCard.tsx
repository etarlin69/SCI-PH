import type { ReactNode } from 'react';
import { cn } from '../../utils/cn';

export interface StatCardProps {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  tone?: 'default' | 'success' | 'warning' | 'danger';
  className?: string;
}

const TONE = { default: 'text-ink', success: 'text-success-600', warning: 'text-warning-700', danger: 'text-danger-text' };

/** Indicador compacto para las cabeceras de lista (p. ej. "Pendientes 4"). */
export function StatCard({ label, value, hint, tone = 'default', className }: StatCardProps) {
  return (
    <div className={cn('flex min-w-0 flex-col gap-1 rounded-xl border border-line bg-white px-4 py-3', className)}>
      <span className="text-xs font-medium text-ink-muted">{label}</span>
      <span className={cn('tabular text-xl font-semibold', TONE[tone])}>{value}</span>
      {hint && <span className="text-xs text-ink-muted">{hint}</span>}
    </div>
  );
}
