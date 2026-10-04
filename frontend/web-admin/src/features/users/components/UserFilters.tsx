import type { Role, User, UserFilters as Filters, UserStatus } from '../../../types';
import { Button, ResponsiveFilters, SearchInput, SegmentedFilter, Select } from '../../../components/shared';
import { USER_STATUSES, activeUserFilterCount, filterUsers } from '../filters';

export interface UserFiltersProps {
  value: Filters;
  onChange: (value: Filters) => void;
  users: User[];
  roles: Role[];
  resultCount: number;
}

const STATUS_LABEL: Record<UserStatus, string> = {
  Activo: 'Activos',
  Pendiente: 'Pendientes',
  Suspendido: 'Suspendidos',
  Inactivo: 'Inactivos',
};

/** Búsqueda + estado (segmentos con contador) + rol. */
export function UserFilters({ value, onChange, users, roles, resultCount }: UserFiltersProps) {
  const base = filterUsers(users, value, true);
  const set = (patch: Partial<Filters>) => onChange({ ...value, ...patch });
  const active = activeUserFilterCount(value);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
        <div className="flex flex-1 flex-wrap items-end gap-3">
          <SearchInput label="Buscar usuarios" placeholder="Buscar por nombre o email" value={value.q} onChange={(q) => set({ q })} />
          <ResponsiveFilters activeCount={value.rol ? 1 : 0} onClear={() => set({ rol: '' })} resultCount={resultCount}>
            <Select
              label="Rol"
              layout="inline"
              value={value.rol}
              onChange={(e) => set({ rol: e.target.value })}
              options={[{ value: '', label: 'Todos los roles' }, ...roles.map((r) => ({ value: r.nombre, label: r.nombre }))]}
              containerClassName="sm:min-w-[220px]"
            />
          </ResponsiveFilters>
        </div>
        {active > 0 && (
          <Button variant="ghost" size="sm" onClick={() => onChange({ q: '', rol: '', estado: '' })}>
            Limpiar filtros
          </Button>
        )}
      </div>
      <SegmentedFilter<UserStatus | ''>
        label="Filtrar por estado"
        value={value.estado}
        onChange={(estado) => set({ estado })}
        options={[
          { value: '', label: 'Todos', count: base.length },
          ...USER_STATUSES.map((s) => ({ value: s, label: STATUS_LABEL[s], count: base.filter((u) => u.estado === s).length })),
        ]}
      />
    </div>
  );
}
