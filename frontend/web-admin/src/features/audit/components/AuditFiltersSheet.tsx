import { useEffect, useState } from 'react';
import type { AuditAction, AuditFilters, AuditResult } from '../../../types';
import { Button, DateRangePicker, Modal, SegmentedFilter, Select, type DateRangePreset } from '../../../components/shared';
import { formatNumber } from '../../../utils/formatters';
import { actionOptions } from '../auditFilters';

export interface AuditFiltersSheetProps {
  open: boolean;
  onClose: () => void;
  value: AuditFilters;
  defaults: AuditFilters;
  onApply: (filters: AuditFilters) => void;
  countFor: (filters: AuditFilters) => number;
  users: string[];
  actions: AuditAction[];
  presets: DateRangePreset[];
  minDate: string;
}

/** Hoja inferior de filtros en móvil (periodo, usuario, acción, resultado). */
export function AuditFiltersSheet({
  open,
  onClose,
  value,
  defaults,
  onApply,
  countFor,
  users,
  actions,
  presets,
  minDate,
}: AuditFiltersSheetProps) {
  const [draft, setDraft] = useState<AuditFilters>(value);

  // Cada vez que se abre, parte de los filtros aplicados.
  useEffect(() => {
    if (open) setDraft(value);
  }, [open, value]);

  const set = (patch: Partial<AuditFilters>) => setDraft((d) => ({ ...d, ...patch }));
  const preview = countFor(draft);

  return (
    <Modal
      open={open}
      onClose={onClose}
      placement="bottom"
      title="Filtros"
      footer={
        <>
          <Button variant="secondary" onClick={() => set({ from: defaults.from, to: defaults.to, usuario: '', accion: '', resultado: '' })}>
            Limpiar
          </Button>
          <Button onClick={() => onApply(draft)} className="flex-1">
            Ver {formatNumber(preview)} eventos
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-5">
        <DateRangePicker
          label="Periodo"
          layout="stacked"
          value={{ from: draft.from, to: draft.to }}
          onChange={(r) => set({ from: r.from, to: r.to })}
          presets={presets}
          min={minDate}
        />
        <Select
          label="Usuario"
          placeholder="Todos los usuarios"
          options={users.map((u) => ({ value: u, label: u }))}
          value={draft.usuario}
          onChange={(e) => set({ usuario: e.target.value })}
        />
        <Select
          label="Tipo de acción"
          placeholder="Todas"
          options={actionOptions(actions)}
          value={draft.accion}
          onChange={(e) => set({ accion: e.target.value as AuditAction | '' })}
        />
        <div className="flex flex-col gap-1.5">
          <span className="text-[13px] font-semibold text-ink-2" aria-hidden="true">
            Resultado
          </span>
          <SegmentedFilter<AuditResult | ''>
            label="Resultado"
            value={draft.resultado}
            onChange={(resultado) => set({ resultado })}
            options={[
              { value: '', label: 'Todos' },
              { value: 'Éxito', label: 'Éxito' },
              { value: 'Error', label: 'Error' },
            ]}
          />
        </div>
      </div>
    </Modal>
  );
}
