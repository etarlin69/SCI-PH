import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Plus, SearchX } from 'lucide-react';
import { Button, EmptyState, ErrorState, PageHeader, SearchInput, Select, Table, useToast, type Column } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue';
import { CLINIC_TYPE_LABELS, ClinicStatusBadge } from '../components/ClinicBadges';
import { ClinicFormModal } from '../components/ClinicFormModal';
import { clinicsService } from '../services/clinicsService';
import type { ClinicAdmin, ClinicStatus } from '../types/clinics.types';

const COLUMNS: Column<ClinicAdmin>[] = [
  {
    id: 'name',
    header: 'Clínica',
    cell: (c) => (
      <span className="flex min-w-0 flex-col">
        <Link to={`/admin/clinicas/${c.id}`} className="truncate font-semibold text-ink hover:underline">
          {c.name}
        </Link>
        {c.facilityCode && <span className="truncate text-xs text-ink-muted">{c.facilityCode}</span>}
      </span>
    ),
  },
  { id: 'type', header: 'Tipo', cell: (c) => CLINIC_TYPE_LABELS[c.type] },
  { id: 'place', header: 'Ubicación', cell: (c) => `${c.municipality}, ${c.province}` },
  { id: 'staff', header: 'Personal afiliado', cell: (c) => <span className="tabular">{c.activeStaffCount}</span> },
  { id: 'status', header: 'Estado', cell: (c) => <ClinicStatusBadge status={c.status} /> },
];

const STATUS_OPTIONS = [
  { value: 'Active', label: 'Activas' },
  { value: 'Inactive', label: 'Inactivas' },
];

/** Clínicas de la red (GET /api/web/admin/clinics, Clinics.Manage). Alta y estado; no hay edición ni borrado. */
export function ClinicsPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<ClinicStatus | ''>('');
  const [creating, setCreating] = useState(false);
  const debounced = useDebouncedValue(search.trim(), 300);
  const q = useAsync(() => clinicsService.list({ search: debounced, status: status || undefined }), [debounced, status]);

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Clínicas"
        description="Establecimientos de la red. El personal se afilia a ellos desde la ficha de Personal."
        actions={
          <Button leftIcon={<Plus size={16} aria-hidden="true" />} onClick={() => setCreating(true)}>
            Nueva clínica
          </Button>
        }
      />

      <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
        <div className="max-w-md flex-1">
          <SearchInput label="Buscar por nombre, código o ubicación" value={search} onChange={setSearch} placeholder="Buscar clínicas" />
        </div>
        <div className="sm:w-48">
          <Select
            label="Estado"
            value={status}
            onChange={(e) => setStatus(e.target.value as ClinicStatus | '')}
            options={STATUS_OPTIONS}
            placeholder="Todas"
          />
        </div>
      </div>

      {q.error ? (
        <ErrorState message={q.error} onRetry={q.reload} />
      ) : (
        <Table
          caption="Clínicas"
          columns={COLUMNS}
          rows={q.data ?? []}
          getRowId={(c) => c.id}
          loading={q.loading}
          onRowClick={(c) => navigate(`/admin/clinicas/${c.id}`)}
          emptyState={<EmptyState icon={SearchX} title={debounced || status ? 'Sin coincidencias' : 'No hay clínicas registradas'} />}
          stickyFirstColumn
        />
      )}

      <ClinicFormModal
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(clinic) => {
          setCreating(false);
          toast({ title: 'Clínica creada', description: clinic.name, tone: 'success' });
          q.reload();
        }}
      />
    </div>
  );
}

export default ClinicsPage;
