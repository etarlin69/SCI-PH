import { SlidersHorizontal, X } from 'lucide-react';
import type { AuditAction, AuditFilters, AuditResult } from '../../../types';
import { Button, DateRangePicker, SearchInput, SegmentedFilter, Select, type DateRangePreset } from '../../../components/shared';
import { cn } from '../../../utils/cn';
import { actionOptions } from '../auditFilters';

export interface AuditFilterBarProps {
  filters: AuditFilters;
  onChange: (patch: Partial<AuditFilters>) => void;
  users: string[];
  actions: AuditAction[];
  counts: Record<AuditResult | '', number>;
  presets: DateRangePreset[];
  minDate: string;
  hasFilters: boolean;
  onClear: () => void;
  activeCount: number;
  onOpenSheet: () => void;
  mobile?: boolean;
}

/** Barra de filtros del registro de auditoría: búsqueda, fechas, usuario, acción y resultado. */
export function AuditFilterBar({
  filters,
  onChange,
  users,
  actions,
  counts,
  presets,
  minDate,
  hasFilters,
  onClear,
  activeCount,
  onOpenSheet,
  mobile = false,
}: AuditFilterBarProps) {
  const search = (
    <SearchInput
      label="Buscar en auditoría"
      value={filters.q}
      onChange={(q) => onChange({ q })}
      placeholder="Usuario, recurso, IP, descripción…"
      className={mobile ? 'min-w-0 flex-1' : undefined}
    />
  );

  if (mobile) {
    return (
      <div role="search" aria-label="Filtros de auditoría" className="flex items-center gap-2">
        {search}
        <button
          type="button"
          onClick={onOpenSheet}
          aria-haspopup="dialog"
          aria-label={`Filtros, ${activeCount ? `${activeCount} activos` : 'ninguno activo'}`}
          className={cn(
            'flex h-10 shrink-0 items-center gap-1.5 rounded-lg border px-3 text-[13px] font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-600',
            activeCount ? 'border-primary-600 bg-primary-600 text-white' : 'border-line-strong bg-white text-ink-2',
          )}
        >
          <SlidersHorizontal size={16} aria-hidden="true" />
          Filtros{activeCount > 0 && <span className="tabular">({activeCount})</span>}
        </button>
      </div>
    );
  }

  return (
    <div role="search" aria-label="Filtros de auditoría" className="flex flex-wrap items-end gap-x-4 gap-y-3">
      <div className="flex flex-col gap-1.5">
        <span className="text-xs font-semibold text-ink-3" aria-hidden="true">
          Búsqueda
        </span>
        {search}
      </div>
      <DateRangePicker
        label="Fecha"
        value={{ from: filters.from, to: filters.to }}
        onChange={(r) => onChange({ from: r.from, to: r.to })}
        presets={presets}
        min={minDate}
      />
      <Select
        label="Usuario"
        placeholder="Todos los usuarios"
        options={users.map((u) => ({ value: u, label: u }))}
        value={filters.usuario}
        onChange={(e) => onChange({ usuario: e.target.value })}
        containerClassName="w-48"
      />
      <Select
        label="Tipo de acción"
        placeholder="Todas"
        options={actionOptions(actions)}
        value={filters.accion}
        onChange={(e) => onChange({ accion: e.target.value as AuditAction | '' })}
        containerClassName="w-44"
      />
      <div className="flex flex-col gap-1.5">
        <span className="text-xs font-semibold text-ink-3" aria-hidden="true">
          Resultado
        </span>
        <SegmentedFilter<AuditResult | ''>
          label="Resultado"
          value={filters.resultado}
          onChange={(resultado) => onChange({ resultado })}
          options={[
            { value: '', label: 'Todos', count: counts[''] },
            { value: 'Éxito', label: 'Éxito', count: counts['Éxito'] },
            { value: 'Error', label: 'Error', count: counts.Error },
          ]}
        />
      </div>
      {hasFilters && (
        <Button variant="secondary" size="sm" leftIcon={<X size={14} aria-hidden="true" />} onClick={onClear}>
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}
