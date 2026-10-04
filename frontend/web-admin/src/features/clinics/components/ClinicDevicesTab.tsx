import { Link } from 'react-router-dom';
import { Tablet } from 'lucide-react';
import type { Device } from '../../../types';
import { clinicsService } from '../../../services';
import { Badge, DEVICE_STATUS_TONE, EmptyState, ErrorState, Table, type Column } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useIsMobile } from '../../../hooks/useMediaQuery';

export interface ClinicDevicesTabProps {
  clinicId: string;
}

function shortDateTime(value: string | null): string {
  if (!value) return 'Nunca';
  const [date, time = ''] = value.split(' ');
  const [, m, d] = date.split('-');
  return `${d}/${m} ${time.slice(0, 5)}`.trim();
}

/** Pestaña Dispositivos del detalle de clínica, con enlace a la ficha de cada dispositivo. */
export function ClinicDevicesTab({ clinicId }: ClinicDevicesTabProps) {
  const isMobile = useIsMobile();
  const { data, loading, error, reload } = useAsync(() => clinicsService.listDevices(clinicId), [clinicId]);

  if (error) return <ErrorState title="No se pudieron cargar los dispositivos" message={error} onRetry={reload} />;

  const columns: Column<Device>[] = [
    {
      id: 'nombre',
      header: 'Nombre',
      sortValue: (d) => d.nombre,
      cell: (d) => (
        <span className="flex flex-col py-1">
          <Link to={`/admin/dispositivos/${d.id}`} className="font-semibold text-primary-600 hover:underline">
            {d.nombre}
          </Link>
          <span className="font-mono text-xs text-ink-muted">{d.id}</span>
        </span>
      ),
    },
    { id: 'tipo', header: 'Tipo', sortValue: (d) => d.tipo, cell: (d) => d.tipo },
    {
      id: 'estado',
      header: 'Estado',
      sortValue: (d) => d.estado,
      cell: (d) => <Badge tone={DEVICE_STATUS_TONE[d.estado]}>{d.estado}</Badge>,
    },
    {
      id: 'acceso',
      header: 'Último acceso',
      sortValue: (d) => d.ultimoAcceso ?? '',
      cell: (d) => <span className="tabular">{shortDateTime(d.ultimoAcceso)}</span>,
    },
  ];

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2 text-[13px]">
        <span className="tabular text-ink-muted" aria-live="polite">
          {loading ? 'Cargando…' : `${data?.length ?? 0} ${data?.length === 1 ? 'dispositivo asignado' : 'dispositivos asignados'}`}
        </span>
        <Link to="/admin/dispositivos" className="font-semibold text-primary-600 hover:underline">
          Gestionar en Dispositivos
        </Link>
      </div>
      <Table
        caption="Dispositivos asignados"
        columns={columns}
        rows={data ?? []}
        getRowId={(d) => d.id}
        loading={loading}
        initialSort={{ columnId: 'nombre', direction: 'asc' }}
        stickyFirstColumn={isMobile}
        minWidth="560px"
        emptyState={
          <EmptyState
            icon={Tablet}
            title="Sin dispositivos asignados"
            description="Vincula tablets o equipos a esta clínica desde Dispositivos."
          />
        }
      />
    </div>
  );
}
