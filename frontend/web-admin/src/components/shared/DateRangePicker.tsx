import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { CalendarDays, ChevronLeft, ChevronRight } from 'lucide-react';
import type { DateRange, ISODate } from '../../types';
import { cn } from '../../utils/cn';
import { WEEKDAYS_ES, formatDMY, formatLong, monthGrid, monthLabel, parseISO, todayISO } from '../../utils/dates';

export interface DateRangePreset {
  label: string;
  range: DateRange;
}

export interface DateRangePickerProps {
  value: DateRange;
  onChange: (range: DateRange) => void;
  min?: ISODate;
  max?: ISODate;
  presets?: DateRangePreset[];
  label?: string;
  footnote?: string;
  layout?: 'inline' | 'stacked';
}

type Field = 'from' | 'to';

/** Selector de rango con dos campos independientes (Desde / Hasta), cada uno con su calendario. */
export function DateRangePicker({
  value,
  onChange,
  min,
  max = todayISO(),
  presets = [],
  label = 'Rango de fechas',
  footnote,
  layout = 'inline',
}: DateRangePickerProps) {
  const [open, setOpen] = useState<Field | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);

  // Cierra el calendario al hacer clic fuera del componente.
  useEffect(() => {
    if (!open) return;
    const onPointerDown = (e: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(null);
    };
    document.addEventListener('mousedown', onPointerDown);
    return () => document.removeEventListener('mousedown', onPointerDown);
  }, [open]);

  const select = (field: Field, iso: ISODate) => {
    if (field === 'from') onChange({ from: iso, to: iso > value.to ? iso : value.to });
    else onChange({ from: value.from, to: iso });
    setOpen(null);
  };

  const trigger = (field: Field) => {
    const iso = field === 'from' ? value.from : value.to;
    const isOpen = open === field;
    return (
      <div className="relative">
        <button
          type="button"
          aria-haspopup="dialog"
          aria-expanded={isOpen}
          aria-label={`Fecha ${field === 'from' ? 'desde' : 'hasta'} ${formatDMY(iso)}, abrir calendario`}
          onClick={() => setOpen(isOpen ? null : field)}
          className={cn(
            'flex h-9 items-center gap-2 rounded-md border bg-white px-3 text-[13px] text-ink hover:bg-surface-hover',
            layout === 'inline' ? 'w-44' : 'w-full',
            isOpen ? 'border-primary-600 ring-[3px] ring-primary-200' : 'border-line-strong',
          )}
        >
          <CalendarDays size={16} className="shrink-0 text-ink-3" aria-hidden="true" />
          <span className="text-xs text-ink-muted">{field === 'from' ? 'Desde' : 'Hasta'}</span>
          <span className="tabular font-medium">{formatDMY(iso)}</span>
        </button>
        {isOpen && (
          <CalendarPopover
            field={field}
            value={value}
            min={field === 'to' ? (min && min > value.from ? min : value.from) : min}
            max={max}
            footnote={footnote}
            onSelect={(d) => select(field, d)}
            onClose={() => setOpen(null)}
          />
        )}
      </div>
    );
  };

  return (
    <div ref={rootRef} className="flex flex-col gap-1.5">
      <span className="text-xs font-semibold text-ink-3">{label}</span>
      <div className={cn('flex gap-2', layout === 'inline' ? 'flex-wrap items-center' : 'flex-col items-stretch')}>
        {trigger('from')}
        {layout === 'inline' && (
          <span className="text-ink-muted" aria-hidden="true">
            –
          </span>
        )}
        {trigger('to')}
        {presets.length > 0 && (
          <div role="group" aria-label="Rangos rápidos" className={cn('flex flex-wrap gap-1.5', layout === 'inline' ? 'sm:ml-2' : '')}>
            {presets.map((p) => {
              const active = p.range.from === value.from && p.range.to === value.to;
              return (
                <button
                  key={p.label}
                  type="button"
                  aria-pressed={active}
                  onClick={() => {
                    onChange(p.range);
                    setOpen(null);
                  }}
                  className={cn(
                    'h-9 whitespace-nowrap rounded-md border px-3.5 text-[13px] font-medium',
                    active ? 'border-primary-600 bg-primary-600 text-white' : 'border-line-strong bg-white text-ink hover:bg-surface-hover',
                  )}
                >
                  {p.label}
                </button>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}

interface CalendarPopoverProps {
  field: Field;
  value: DateRange;
  min?: ISODate;
  max: ISODate;
  footnote?: string;
  onSelect: (iso: ISODate) => void;
  onClose: () => void;
}

function CalendarPopover({ field, value, min, max, footnote, onSelect, onClose }: CalendarPopoverProps) {
  const initial = parseISO(field === 'from' ? value.from : value.to);
  const [view, setView] = useState({ year: initial.getFullYear(), month: initial.getMonth() });
  const today = todayISO();

  const monthIndex = view.year * 12 + view.month;
  const minDate = min ? parseISO(min) : null;
  const maxDate = parseISO(max);
  const canPrev = !minDate || monthIndex > minDate.getFullYear() * 12 + minDate.getMonth();
  const canNext = monthIndex < maxDate.getFullYear() * 12 + maxDate.getMonth();
  const shift = (delta: number) => {
    const m = monthIndex + delta;
    setView({ year: Math.floor(m / 12), month: m % 12 });
  };

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Escape') {
      e.stopPropagation();
      onClose();
    }
  };

  const navBtn =
    'flex h-8 w-8 items-center justify-center rounded-md bg-surface-hover text-ink-3 hover:bg-surface-pressed disabled:cursor-not-allowed disabled:bg-surface-ground disabled:text-[#AEB8C4]';

  return (
    <div
      role="dialog"
      aria-label={field === 'from' ? 'Seleccionar fecha desde' : 'Seleccionar fecha hasta'}
      onKeyDown={onKeyDown}
      className="absolute left-0 top-full z-popover mt-2 flex w-[300px] flex-col gap-2.5 rounded-[10px] border border-line bg-white p-3.5 shadow-[0_16px_40px_rgba(13,31,51,0.18)]"
    >
      <div className="flex items-center justify-between">
        <button type="button" className={navBtn} disabled={!canPrev} onClick={() => shift(-1)} aria-label="Mes anterior">
          <ChevronLeft size={16} aria-hidden="true" />
        </button>
        <span className="text-sm font-semibold" aria-live="polite">
          {monthLabel(view.year, view.month)}
        </span>
        <button type="button" className={navBtn} disabled={!canNext} onClick={() => shift(1)} aria-label="Mes siguiente">
          <ChevronRight size={16} aria-hidden="true" />
        </button>
      </div>
      <p className="text-xs text-ink-3">
        {field === 'from' ? 'Elige la fecha de inicio' : `Elige la fecha de fin (desde ${formatDMY(value.from)})`}
      </p>
      <div className="grid grid-cols-7 gap-0.5 text-center">
        {WEEKDAYS_ES.map((w) => (
          <span key={w} className="py-1 text-[11px] font-semibold text-ink-muted">
            {w}
          </span>
        ))}
        {monthGrid(view.year, view.month).map((cell, i) => {
          if (!cell) return <span key={`blank-${i}`} />;
          const disabled = (min !== undefined && cell.iso < min) || cell.iso > max;
          const isEdge = cell.iso === value.from || cell.iso === value.to;
          const inRange = cell.iso > value.from && cell.iso < value.to;
          return (
            <button
              key={cell.iso}
              type="button"
              disabled={disabled}
              aria-pressed={isEdge}
              aria-label={formatLong(cell.iso)}
              aria-current={cell.iso === today ? 'date' : undefined}
              onClick={() => onSelect(cell.iso)}
              className={cn(
                'tabular h-[34px] rounded-md text-[13px]',
                disabled && 'cursor-not-allowed text-[#AEB8C4]',
                !disabled && isEdge && 'bg-primary-600 font-semibold text-white',
                !disabled && !isEdge && inRange && 'bg-primary-100 text-primary-700 hover:bg-primary-200',
                !disabled && !isEdge && !inRange && 'text-ink hover:bg-surface-hover',
                cell.iso === today && !isEdge && 'ring-1 ring-inset ring-primary-300',
              )}
            >
              {cell.day}
            </button>
          );
        })}
      </div>
      <div className="flex items-center justify-between text-xs text-ink-muted">
        <span>{footnote}</span>
        <button
          type="button"
          onClick={onClose}
          className="h-[30px] rounded-md border border-line-strong px-2.5 font-medium text-ink hover:bg-surface-hover"
        >
          Cerrar
        </button>
      </div>
    </div>
  );
}
