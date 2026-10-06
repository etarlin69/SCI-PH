import { PUESTOS } from '../../../data/mock';
import type { Clinic, Puesto, StaffFilters as Filters, StaffMember, StaffStatus } from '../../../types';
import { Button, ResponsiveFilters, SearchInput, SegmentedFilter, Select } from '../../../components/shared';
import { STAFF_STATUSES, activeStaffFilters, filterStaff } from '../filters';

export interface StaffFiltersProps {
  value: Filters;
  onChange: (value: Filters) => void;
  staff: StaffMember[];
  clinics: Pick<Clinic, 'id' | 'nombre'>[];
  resultCount: number;
}

/** Búsqueda + puesto y clínica (hoja inferior en móvil) + estado con contadores. */
export function StaffFilters({ value, onChange, staff, clinics, resultCount }: StaffFiltersProps) {
  const set = (patch: Partial<Filters>) => onChange({ ...value, ...patch });
  const base = filterStaff(staff, { ...value, estado: '' });
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
        <div className="flex flex-1 flex-wrap items-end gap-3">
          <SearchInput label="Buscar personal" placeholder="Buscar por nombre o email" value={value.q} onChange={(q) => set({ q })} />
          <ResponsiveFilters
            activeCount={[value.puesto, value.clinicId].filter(Boolean).length}
            onClear={() => set({ puesto: '', clinicId: '' })}
            resultCount={resultCount}
          >
            <Select
              label="Puesto"
              layout="inline"
              value={value.puesto}
              onChange={(e) => set({ puesto: e.target.value as Puesto | '' })}
              options={[{ value: '', label: 'Todos' }, ...PUESTOS.map((p) => ({ value: p, label: p }))]}
              containerClassName="sm:min-w-[230px]"
            />
            <Select
              label="Clínica"
              layout="inline"
              value={value.clinicId}
              onChange={(e) => set({ clinicId: e.target.value })}
              options={[{ value: '', label: 'Todas' }, ...clinics.map((c) => ({ value: c.id, label: c.nombre }))]}
              containerClassName="sm:min-w-[210px]"
            />
          </ResponsiveFilters>
        </div>
        {activeStaffFilters(value) > 0 && (
          <Button variant="ghost" size="sm" onClick={() => onChange({ q: '', puesto: '', clinicId: '', estado: '' })}>
            Limpiar
          </Button>
        )}
      </div>
      <SegmentedFilter<StaffStatus | ''>
        label="Filtrar por estado"
        value={value.estado}
        onChange={(estado) => set({ estado })}
        options={[
          { value: '', label: 'Todos', count: base.length },
          ...STAFF_STATUSES.map((s) => ({ value: s, label: s, count: base.filter((x) => x.estado === s).length })),
        ]}
      />
    </div>
  );
}
