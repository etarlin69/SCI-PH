import { Link } from 'react-router-dom';
import { Users } from 'lucide-react';
import type { StaffMember } from '../../../types';
import { clinicsService } from '../../../services';
import { Badge, EmptyState, ErrorState, STAFF_STATUS_TONE, Table, type Column } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useIsMobile } from '../../../hooks/useMediaQuery';

export interface ClinicStaffTabProps {
  clinicId: string;
}

/** Pestaña Personal del detalle de clínica: personas asignadas con enlace a su ficha. */
export function ClinicStaffTab({ clinicId }: ClinicStaffTabProps) {
  const isMobile = useIsMobile();
  const { data, loading, error, reload } = useAsync(() => clinicsService.listStaff(clinicId), [clinicId]);

  if (error) return <ErrorState title="No se pudo cargar el personal" message={error} onRetry={reload} />;

  const columns: Column<StaffMember>[] = [
    {
      id: 'nombre',
      header: 'Nombre',
      sortValue: (s) => s.nombre.replace(/^(Dr\.|Dra\.)\s*/, ''),
      cell: (s) => (
        <Link to={`/admin/personal/${s.id}`} className="font-semibold text-primary-600 hover:underline">
          {s.nombre}
        </Link>
      ),
    },
    { id: 'puesto', header: 'Puesto', sortValue: (s) => s.puesto, cell: (s) => s.puesto },
    { id: 'departamento', header: 'Departamento', sortValue: (s) => s.departamento, cell: (s) => s.departamento },
    {
      id: 'estado',
      header: 'Estado',
      sortValue: (s) => s.estado,
      cell: (s) => <Badge tone={STAFF_STATUS_TONE[s.estado]}>{s.estado}</Badge>,
    },
  ];

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2 text-[13px]">
        <span className="tabular text-ink-muted" aria-live="polite">
          {loading ? 'Cargando…' : `${data?.length ?? 0} ${data?.length === 1 ? 'persona asignada' : 'personas asignadas'}`}
        </span>
        <Link to={`/admin/personal?clinica=${encodeURIComponent(clinicId)}`} className="font-semibold text-primary-600 hover:underline">
          Ver todo en Personal
        </Link>
      </div>
      <Table
        caption="Personal asignado"
        columns={columns}
        rows={data ?? []}
        getRowId={(s) => s.id}
        loading={loading}
        initialSort={{ columnId: 'nombre', direction: 'asc' }}
        pageSize={10}
        stickyFirstColumn={isMobile}
        minWidth="640px"
        emptyState={<EmptyState icon={Users} title="Sin personal asignado" description="Asigna personal a esta clínica desde Personal." />}
      />
    </div>
  );
}
