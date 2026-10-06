import { useRef, type KeyboardEvent } from 'react';
import { cn } from '../../utils/cn';

export interface TabItem<V extends string> {
  value: V;
  label: string;
  count?: number;
}

export interface TabsProps<V extends string> {
  label: string;
  tabs: TabItem<V>[];
  value: V;
  onChange: (value: V) => void;
  idPrefix: string;
  variant?: 'underline' | 'pill';
}

/** Id del panel activo, para `<div role="tabpanel" id=... aria-labelledby=...>`. */
export const tabIds = (idPrefix: string, value: string) => ({ tab: `${idPrefix}-tab-${value}`, panel: `${idPrefix}-panel` });

/** Pestañas accesibles (roving tabindex; ←/→, Inicio y Fin). */
export function Tabs<V extends string>({ label, tabs, value, onChange, idPrefix, variant = 'underline' }: TabsProps<V>) {
  const refs = useRef<(HTMLButtonElement | null)[]>([]);

  const onKeyDown = (e: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const last = tabs.length - 1;
    const next =
      e.key === 'ArrowRight'
        ? index === last
          ? 0
          : index + 1
        : e.key === 'ArrowLeft'
          ? index === 0
            ? last
            : index - 1
          : e.key === 'Home'
            ? 0
            : e.key === 'End'
              ? last
              : null;
    if (next === null) return;
    e.preventDefault();
    onChange(tabs[next].value);
    refs.current[next]?.focus();
  };

  return (
    <div
      role="tablist"
      aria-label={label}
      className={cn('flex gap-1', variant === 'underline' ? 'border-b border-line' : 'w-fit rounded-lg bg-surface-hover p-1')}
    >
      {tabs.map((t, i) => {
        const selected = t.value === value;
        const ids = tabIds(idPrefix, t.value);
        return (
          <button
            key={t.value}
            ref={(el) => (refs.current[i] = el)}
            id={ids.tab}
            type="button"
            role="tab"
            aria-selected={selected}
            aria-controls={ids.panel}
            tabIndex={selected ? 0 : -1}
            onClick={() => onChange(t.value)}
            onKeyDown={(e) => onKeyDown(e, i)}
            className={cn(
              'flex items-center gap-2 whitespace-nowrap text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600',
              variant === 'underline'
                ? cn(
                    '-mb-px h-11 border-b-2 px-4',
                    selected ? 'border-primary-600 font-semibold text-ink' : 'border-transparent font-medium text-ink-3 hover:text-ink',
                  )
                : cn(
                    'h-9 rounded-md px-3.5',
                    selected ? 'bg-white font-semibold text-ink shadow-sm' : 'font-medium text-ink-3 hover:text-ink',
                  ),
            )}
          >
            {t.label}
            {t.count !== undefined && (
              <span
                className={cn(
                  'tabular rounded-full px-2 py-px text-xs font-semibold',
                  selected ? 'bg-primary-600 text-white' : 'bg-line text-ink-2',
                )}
              >
                {t.count}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
