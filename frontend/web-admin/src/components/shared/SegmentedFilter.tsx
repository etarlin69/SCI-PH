import { cn } from '../../utils/cn';

export interface SegmentedOption<V extends string> {
  value: V;
  label: string;
  count?: number;
}

export interface SegmentedFilterProps<V extends string> {
  label: string;
  options: SegmentedOption<V>[];
  value: V;
  onChange: (value: V) => void;
  className?: string;
}

/** Filtro rápido por estado (botones con `aria-pressed` y contador). */
export function SegmentedFilter<V extends string>({ label, options, value, onChange, className }: SegmentedFilterProps<V>) {
  return (
    <div role="group" aria-label={label} className={cn('relative -mx-1 flex gap-1.5 overflow-x-auto px-1 pb-0.5', className)}>
      {options.map((o) => {
        const active = o.value === value;
        return (
          <button
            key={o.value}
            type="button"
            aria-pressed={active}
            onClick={() => onChange(o.value)}
            className={cn(
              'flex h-8 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-full border px-3 text-[13px] font-medium',
              'focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-primary-600',
              active ? 'border-primary-600 bg-primary-600 text-white' : 'border-line bg-white text-ink-3 hover:bg-surface-hover',
            )}
          >
            {o.label}
            {o.count !== undefined && (
              <span
                className={cn(
                  'tabular rounded-full px-1.5 text-[11px] font-semibold',
                  active ? 'bg-white/20' : 'bg-surface-hover text-ink-muted',
                )}
              >
                {o.count}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
