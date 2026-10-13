import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Plus, SearchX } from 'lucide-react';
import { Button, EmptyState, ErrorState, PageHeader, SearchInput, Select, Table, useToast, type Column } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue';
import { AccountBadge, STAFF_TYPE_LABELS, StaffStatusBadge, staffFullName } from '../components/StaffBadges';
import { StaffFormModal } from '../components/StaffFormModal';
import { staffService } from '../services/staffService';
import type { ClinicalStaffAdmin, StaffStatus } from '../types/staff.types';

const COLUMNS: Column<ClinicalStaffAdmin>[] = [
  {
    id: 'name',
    header: 'Personal',
    cell: (s) => (
      <span className="flex min-w-0 flex-col">
        <Link to={`/admin/personal/${s.id}`} className="truncate font-semibold text-ink hover:underline">
          {staffFullName(s)}
        </Link>
        {s.professionalLicenseNumber && <span className="truncate text-xs text-ink-muted">Licencia {s.professionalLicenseNumber}</span>}
      </span>
    ),
  },
  { id: 'type', header: 'Tipo', cell: (s) => STAFF_TYPE_LABELS[s.type] },
  {
    id: 'clinics',
    header: 'Clínicas activas',
    cell: (s) => (s.activeClinics.length === 0 ? <span className="text-ink-muted">Sin afiliación</span> : s.activeClinics.map((c) => c.name).join(', ')),
  },
  { id: 'account', header: 'Cuenta', cell: (s) => <AccountBadge linked={s.accountLinked} /> },
  { id: 'status', header: 'Estado', cell: (s) => <StaffStatusBadge status={s.status} /> },
];

const STATUS_OPTIONS = [
  { value: 'Active', label: 'Activos' },
  { value: 'Inactive', label: 'Inactivos' },
];

/** Personal clínico (GET /api/web/admin/staff, ClinicalStaff.Manage). Alta, estado, afiliaciones e invitación de cuenta. */
export function StaffPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<StaffStatus | ''>('');
  const [creating, setCreating] = useState(false);
  const debounced = useDebouncedValue(search.trim(), 300);
  const q = useAsync(() => staffService.list({ search: debounced, status: status || undefined }), [debounced, status]);

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Personal"
        description="Personal clínico de la red, sus clínicas y su cuenta de acceso."
        actions={
          <Button leftIcon={<Plus size={16} aria-hidden="true" />} onClick={() => setCreating(true)}>
            Nuevo personal
          </Button>
        }
      />

      <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
        <div className="max-w-md flex-1">
          <SearchInput label="Buscar por nombre o licencia" value={search} onChange={setSearch} placeholder="Buscar personal" />
        </div>
        <div className="sm:w-48">
          <Select label="Estado" value={status} onChange={(e) => setStatus(e.target.value as StaffStatus | '')} options={STATUS_OPTIONS} placeholder="Todos" />
        </div>
      </div>

      {q.error ? (
        <ErrorState message={q.error} onRetry={q.reload} />
      ) : (
        <Table
          caption="Personal clínico"
          columns={COLUMNS}
          rows={q.data ?? []}
          getRowId={(s) => s.id}
          loading={q.loading}
          onRowClick={(s) => navigate(`/admin/personal/${s.id}`)}
          emptyState={<EmptyState icon={SearchX} title={debounced || status ? 'Sin coincidencias' : 'No hay personal registrado'} />}
          stickyFirstColumn
        />
      )}

      <StaffFormModal
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(staff) => {
          setCreating(false);
          toast({ title: 'Personal creado', description: staffFullName(staff), tone: 'success' });
          navigate(`/admin/personal/${staff.id}`);
        }}
      />
    </div>
  );
}

export default StaffPage;
