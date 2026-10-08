import { X } from 'lucide-react';

export interface FilterChip {
  key: string;
  label: string;
  onRemove: () => void;
}

export interface ActiveFilterChipsProps {
  chips: FilterChip[];
  onClearAll?: () => void;
}

/** Fila de filtros aplicados, cada uno con su botón para quitarlo. */
export function ActiveFilterChips({ chips, onClearAll }: ActiveFilterChipsProps) {
  if (chips.length === 0) return null;
  return (
    <div className="flex items-center gap-2">
      <ul
        aria-label="Filtros aplicados"
        className="-mx-1 flex min-w-0 flex-1 gap-1.5 overflow-x-auto px-1 pb-0.5 sm:flex-wrap sm:overflow-visible"
      >
        {chips.map((c) => (
          <li key={c.key} className="shrink-0">
            <button
              type="button"
              onClick={c.onRemove}
              aria-label={`Quitar filtro ${c.label}`}
              className="flex h-7 items-center gap-1 whitespace-nowrap rounded-full border border-primary-200 bg-primary-50 pl-2.5 pr-1.5 text-xs font-medium text-primary-700 hover:bg-primary-100 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
            >
              {c.label}
              <X size={12} aria-hidden="true" />
            </button>
          </li>
        ))}
      </ul>
      {onClearAll && (
        <button
          type="button"
          onClick={onClearAll}
          className="shrink-0 rounded px-1 text-xs font-semibold text-primary-600 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
        >
          Limpiar filtros
        </button>
      )}
    </div>
  );
}
