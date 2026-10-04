import type { Device, DeviceFilters as Filters, DeviceStatus, DeviceType } from '../../../types';
import { Button, ResponsiveFilters, SearchInput, SegmentedFilter, Select } from '../../../components/shared';
import { countDevicesByStatus, countSecondaryDeviceFilters, filterDevices, hasDeviceFilters } from '../filters';
import { DEVICE_STATUSES, DEVICE_TYPES } from '../helpers';

export interface DeviceFiltersProps {
  value: Filters;
  onChange: (value: Filters) => void;
  devices: Device[];
  resultCount: number;
}

const STATUS_LABEL: Record<DeviceStatus, string> = { Activo: 'Activos', Pendiente: 'Pendientes', Revocado: 'Revocados' };

/** Búsqueda + tipo (hoja inferior en móvil) + estado con contadores. */
export function DeviceFilters({ value, onChange, devices, resultCount }: DeviceFiltersProps) {
  const set = (patch: Partial<Filters>) => onChange({ ...value, ...patch });
  const counts = countDevicesByStatus(filterDevices(devices, { ...value, estado: '' }));
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
        <div className="flex flex-1 flex-wrap items-end gap-3">
          <SearchInput
            label="Buscar dispositivos"
            placeholder="Buscar por nombre, ID o asignación"
            value={value.q}
            onChange={(q) => set({ q })}
          />
          <ResponsiveFilters activeCount={countSecondaryDeviceFilters(value)} onClear={() => set({ tipo: '' })} resultCount={resultCount}>
            <Select
              label="Tipo"
              layout="inline"
              value={value.tipo}
              onChange={(e) => set({ tipo: e.target.value as DeviceType | '' })}
              options={[{ value: '', label: 'Todos los tipos' }, ...DEVICE_TYPES.map((t) => ({ value: t, label: t }))]}
              containerClassName="sm:min-w-[200px]"
            />
          </ResponsiveFilters>
        </div>
        {hasDeviceFilters(value) && (
          <Button variant="ghost" size="sm" onClick={() => onChange({ q: '', tipo: '', estado: '' })}>
            Limpiar filtros
          </Button>
        )}
      </div>
      <SegmentedFilter<DeviceStatus | ''>
        label="Filtrar por estado"
        value={value.estado}
        onChange={(estado) => set({ estado })}
        options={[
          { value: '', label: 'Todos', count: counts[''] },
          ...DEVICE_STATUSES.map((s) => ({ value: s, label: STATUS_LABEL[s], count: counts[s] })),
        ]}
      />
    </div>
  );
}
