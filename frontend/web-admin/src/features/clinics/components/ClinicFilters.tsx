import { useState } from 'react';
import { SlidersHorizontal, X } from 'lucide-react';
import type { Clinic, ClinicStatus, ClinicType } from '../../../types';
import { clinicsService } from '../../../services';
import { Button, Modal, SearchInput, SegmentedFilter, Select } from '../../../components/shared';
import { useIsMobile } from '../../../hooks/useMediaQuery';
import { CLINIC_TYPES, EMPTY_CLINIC_FILTERS, countSecondaryFilters, hasActiveFilters, type ClinicListFilters } from '../filters';

export interface ClinicFiltersProps {
  value: ClinicListFilters;
  onChange: (value: ClinicListFilters) => void;
  clinics: Clinic[];
}

type EstadoOption = ClinicStatus | 'todas';

/** Barra de filtros de Clínicas: búsqueda, estado (segmentado con contadores) y provincia/tipo. */
export function ClinicFilters({ value, onChange, clinics }: ClinicFiltersProps) {
  const isMobile = useIsMobile();
  const [sheetOpen, setSheetOpen] = useState(false);
  const [draft, setDraft] = useState(value);
  const secondary = countSecondaryFilters(value);
  const set = (patch: Partial<ClinicListFilters>) => onChange({ ...value, ...patch });

  const estados: { value: EstadoOption; label: string; count: number }[] = [
    { value: 'todas', label: 'Todas', count: clinics.length },
    { value: 'Activa', label: 'Activas', count: clinics.filter((c) => c.estado === 'Activa').length },
    { value: 'Inactiva', label: 'Inactivas', count: clinics.filter((c) => c.estado === 'Inactiva').length },
  ];

  const provinciaOptions = clinicsService.provincias.map((p) => ({ value: p, label: p }));
  const tipoOptions = CLINIC_TYPES.map((t) => ({ value: t, label: t }));

  const secondaryFields = (v: ClinicListFilters, apply: (patch: Partial<ClinicListFilters>) => void, inline: boolean) => (
    <>
      <Select
        label="Provincia"
        layout={inline ? 'inline' : 'stacked'}
        placeholder="Todas las provincias"
        options={provinciaOptions}
        value={v.provincia}
        onChange={(e) => apply({ provincia: e.target.value })}
        className={inline ? 'w-48' : undefined}
      />
      <Select
        label="Tipo"
        layout={inline ? 'inline' : 'stacked'}
        placeholder="Todos los tipos"
        options={tipoOptions}
        value={v.tipo}
        onChange={(e) => apply({ tipo: e.target.value as ClinicType | '' })}
        className={inline ? 'w-48' : undefined}
      />
    </>
  );

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2.5">
        <SearchInput
          label="Buscar por nombre o dirección"
          placeholder="Buscar por nombre o dirección"
          value={value.q}
          onChange={(q) => set({ q })}
          className="sm:w-80"
        />
        {isMobile ? (
          <Button
            variant="secondary"
            leftIcon={<SlidersHorizontal size={16} aria-hidden="true" />}
            onClick={() => {
              setDraft(value);
              setSheetOpen(true);
            }}
            aria-label={`Filtros${secondary ? ` (${secondary} activos)` : ''}`}
          >
            Filtros{secondary ? ` (${secondary})` : ''}
          </Button>
        ) : (
          secondaryFields(value, set, true)
        )}
        {hasActiveFilters(value) && (
          <Button variant="ghost" size="sm" leftIcon={<X size={14} aria-hidden="true" />} onClick={() => onChange(EMPTY_CLINIC_FILTERS)}>
            Limpiar filtros
          </Button>
        )}
      </div>
      <SegmentedFilter<EstadoOption>
        label="Filtrar por estado"
        options={estados}
        value={value.estado || 'todas'}
        onChange={(v) => set({ estado: v === 'todas' ? '' : v })}
      />

      <Modal
        open={sheetOpen}
        onClose={() => setSheetOpen(false)}
        placement="bottom"
        title="Filtros"
        footer={
          <>
            <Button
              variant="secondary"
              onClick={() => {
                onChange({ ...value, provincia: '', tipo: '' });
                setSheetOpen(false);
              }}
            >
              Limpiar
            </Button>
            <Button
              onClick={() => {
                onChange({ ...value, provincia: draft.provincia, tipo: draft.tipo });
                setSheetOpen(false);
              }}
            >
              Aplicar filtros
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">{secondaryFields(draft, (patch) => setDraft((d) => ({ ...d, ...patch })), false)}</div>
      </Modal>
    </div>
  );
}
