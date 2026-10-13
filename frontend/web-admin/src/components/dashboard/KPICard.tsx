import type { ReactNode } from 'react';
import { ArrowDown, ArrowRight, ArrowUp } from 'lucide-react';
import { cn } from '../../utils/cn';

export type KpiTone = 'positive' | 'negative' | 'neutral';
export type KpiStatus = 'ok' | 'warn' | 'crit';

export interface KPICardProps {
  label: string;
  value: string;
  unit?: string;
  delta?: { value: string; direction: 'up' | 'down' | 'flat'; tone: KpiTone; caption?: string };
  status?: { level: KpiStatus; text: string };
  highlight?: { value: string; caption?: string; tone?: KpiTone };
  caption?: ReactNode;
  loading?: boolean;
}

const TONE: Record<KpiTone, string> = {
  positive: 'bg-success-50 text-success-600',
  negative: 'bg-warning-50 text-warning-700',
  neutral: 'bg-[#EEF0F2] text-ink-3',
};

const STATUS: Record<KpiStatus, { dot: string; text: string }> = {
  ok: { dot: 'bg-success-600', text: 'text-success-600' },
  warn: { dot: 'bg-warning-500', text: 'text-warning-700' },
  crit: { dot: 'bg-danger-600', text: 'text-danger-text' },
};

export function KPICard({ label, value, unit, delta, status, highlight, caption, loading = false }: KPICardProps) {
  const DeltaIcon = delta?.direction === 'up' ? ArrowUp : delta?.direction === 'down' ? ArrowDown : ArrowRight;

  return (
    <div className="flex flex-col gap-2.5 rounded-[10px] border border-line bg-white p-5" aria-busy={loading || undefined}>
      <span className="text-[13px] font-medium text-ink-3">{label}</span>
      {loading ? (
        <span className="block h-9 w-24 animate-pulse rounded bg-line-soft" />
      ) : (
        <span className="flex items-baseline gap-1.5">
          <span className="tabular text-[36px] font-semibold leading-none tracking-tight text-ink">{value}</span>
          {unit && <span className="text-base text-ink-3">{unit}</span>}
        </span>
      )}
      <span className="flex min-h-5 items-center gap-1.5 text-[13px] text-ink-muted">
        {delta && (
          <>
            <span className={cn('tabular inline-flex items-center gap-0.5 rounded px-1.5 py-0.5 font-semibold', TONE[delta.tone])}>
              <DeltaIcon size={12} aria-hidden="true" />
              {delta.value}
            </span>
            {delta.caption}
          </>
        )}
        {highlight && (
          <>
            <span className={cn('tabular rounded px-1.5 py-0.5 font-semibold', TONE[highlight.tone ?? 'positive'])}>{highlight.value}</span>
            {highlight.caption}
          </>
        )}
        {status && (
          <span className={cn('flex items-center gap-1.5 font-medium', STATUS[status.level].text)}>
            <span className={cn('h-2.5 w-2.5 rounded-full', STATUS[status.level].dot)} aria-hidden="true" />
            {status.text}
          </span>
        )}
        {caption && <span className="tabular">{caption}</span>}
      </span>
    </div>
  );
}
