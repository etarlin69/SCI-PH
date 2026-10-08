import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { ChevronDown, X } from 'lucide-react';
import { cn } from '../../utils/cn';

export interface ComboboxOption {
  value: string;
  label: string;
  description?: string;
}

export interface ComboboxProps {
  label: string;
  options: ComboboxOption[];
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  emptyLabel?: string;
}

/** Desplegable con búsqueda (patrón ARIA combobox + listbox). */
export function Combobox({ label, options, value, onChange, placeholder = 'Buscar…', emptyLabel = 'Todos' }: ComboboxProps) {
  const id = useId();
  const listId = `${id}-list`;
  const rootRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);

  const selected = options.find((o) => o.value === value);
  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return q ? options.filter((o) => `${o.label} ${o.description ?? ''}`.toLowerCase().includes(q)) : options;
  }, [options, query]);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (e: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onPointerDown);
    return () => document.removeEventListener('mousedown', onPointerDown);
  }, [open]);

  useEffect(() => setActive(0), [query]);

  const choose = (v: string) => {
    onChange(v);
    setQuery('');
    setOpen(false);
  };

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setOpen(true);
      setActive((a) => Math.min(a + 1, filtered.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActive((a) => Math.max(a - 1, 0));
    } else if (e.key === 'Enter' && open && filtered[active]) {
      e.preventDefault();
      choose(filtered[active].value);
    } else if (e.key === 'Escape' && open) {
      e.stopPropagation();
      setOpen(false);
    }
  };

  return (
    <div ref={rootRef} className="relative flex flex-col gap-1.5">
      <label htmlFor={`${id}-input`} className="text-[13px] font-semibold text-ink-2">
        {label}
      </label>
      <div className="relative">
        <input
          id={`${id}-input`}
          role="combobox"
          aria-expanded={open}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={open && filtered[active] ? `${id}-opt-${active}` : undefined}
          value={open ? query : (selected?.label ?? '')}
          placeholder={open ? placeholder : emptyLabel}
          onFocus={() => setOpen(true)}
          onClick={() => setOpen(true)}
          onChange={(e) => {
            setQuery(e.target.value);
            setOpen(true);
          }}
          onKeyDown={onKeyDown}
          className={cn(
            'h-10 w-full rounded-lg border bg-white pl-3 pr-16 text-sm text-ink placeholder:text-ink-3 focus:outline-none focus:ring-[3px]',
            open ? 'border-primary-600 ring-[3px] ring-primary-200' : 'border-line-strong',
          )}
        />
        <div className="absolute right-2 top-1/2 flex -translate-y-1/2 items-center gap-0.5">
          {value && (
            <button
              type="button"
              onClick={() => choose('')}
              aria-label={`Quitar filtro de ${label.toLowerCase()}`}
              className="flex h-7 w-7 items-center justify-center rounded text-ink-3 hover:bg-surface-hover"
            >
              <X size={14} aria-hidden="true" />
            </button>
          )}
          <ChevronDown size={16} className="pointer-events-none text-ink-3" aria-hidden="true" />
        </div>
      </div>
      {open && (
        <ul
          id={listId}
          role="listbox"
          aria-label={label}
          className="absolute left-0 right-0 top-full z-popover mt-1.5 max-h-64 overflow-y-auto rounded-lg border border-line bg-white p-1 shadow-[0_16px_40px_rgba(13,31,51,0.18)]"
        >
          <li
            role="option"
            aria-selected={value === ''}
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => choose('')}
            className="cursor-pointer rounded-md px-2.5 py-2 text-sm font-semibold text-ink hover:bg-surface-hover"
          >
            {emptyLabel}
          </li>
          {filtered.length === 0 && <li className="px-2.5 py-2 text-sm text-ink-muted">Sin coincidencias</li>}
          {filtered.map((o, i) => (
            <li
              key={o.value}
              id={`${id}-opt-${i}`}
              role="option"
              aria-selected={o.value === value}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => choose(o.value)}
              onMouseEnter={() => setActive(i)}
              className={cn(
                'flex cursor-pointer flex-col rounded-md px-2.5 py-1.5 text-sm',
                i === active && 'bg-surface-hover',
                o.value === value && 'font-semibold text-primary-700',
              )}
            >
              {o.label}
              {o.description && <span className="text-xs font-normal text-ink-muted">{o.description}</span>}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
