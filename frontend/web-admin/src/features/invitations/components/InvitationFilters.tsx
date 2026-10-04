import type { Invitation, InvitationFilters as Filters, InvitationStatus, InvitationType } from '../../../types';
import { Button, ResponsiveFilters, SearchInput, SegmentedFilter, Select } from '../../../components/shared';
import { INVITATION_STATUSES, filterInvitations } from '../helpers';

export interface InvitationFiltersProps {
  value: Filters;
  onChange: (value: Filters) => void;
  invitations: Invitation[];
  resultCount: number;
}

const LABEL: Record<InvitationStatus, string> = {
  Pendiente: 'Pendientes',
  Aceptada: 'Aceptadas',
  Expirada: 'Expiradas',
  Revocada: 'Revocadas',
};

/** Búsqueda por email + tipo (hoja inferior en móvil) + estado con contadores. */
export function InvitationFilters({ value, onChange, invitations, resultCount }: InvitationFiltersProps) {
  const set = (patch: Partial<Filters>) => onChange({ ...value, ...patch });
  const base = filterInvitations(invitations, { ...value, estado: '' });
  const any = !!(value.q || value.tipo || value.estado);
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
        <div className="flex flex-1 flex-wrap items-end gap-3">
          <SearchInput label="Buscar invitaciones" placeholder="Buscar por email" value={value.q} onChange={(q) => set({ q })} />
          <ResponsiveFilters activeCount={value.tipo ? 1 : 0} onClear={() => set({ tipo: '' })} resultCount={resultCount}>
            <Select
              label="Tipo"
              layout="inline"
              value={value.tipo}
              onChange={(e) => set({ tipo: e.target.value as InvitationType | '' })}
              options={[
                { value: '', label: 'Todos' },
                { value: 'Paciente', label: 'Paciente' },
                { value: 'Personal', label: 'Personal' },
              ]}
              containerClassName="sm:min-w-[180px]"
            />
          </ResponsiveFilters>
        </div>
        {any && (
          <Button variant="ghost" size="sm" onClick={() => onChange({ q: '', tipo: '', estado: '' })}>
            Limpiar filtros
          </Button>
        )}
      </div>
      <SegmentedFilter<InvitationStatus | ''>
        label="Filtrar por estado"
        value={value.estado}
        onChange={(estado) => set({ estado })}
        options={[
          { value: '', label: 'Todas', count: base.length },
          ...INVITATION_STATUSES.map((s) => ({ value: s, label: LABEL[s], count: base.filter((i) => i.estado === s).length })),
        ]}
      />
    </div>
  );
}
