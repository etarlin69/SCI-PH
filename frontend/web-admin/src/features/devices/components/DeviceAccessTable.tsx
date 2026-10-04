import { useMemo, useState } from 'react';
import { CheckCircle2, History } from 'lucide-react';
import type { DeviceAccess, DeviceAccessResult } from '../../../types';
import { Badge, Card, EmptyState, ErrorState, SegmentedFilter, Table, type Column } from '../../../components/shared';
import { formatDateTime } from '../helpers';

export interface DeviceAccessTableProps {
  accesses: DeviceAccess[];
  loading: boolean;
  error: string | null;
  onRetry: () => void;
}

/** "Histórico de accesos" con filtro Todos / Éxito / Fallido y paginación. */
export function DeviceAccessTable({ accesses, loading, error, onRetry }: DeviceAccessTableProps) {
  const [filter, setFilter] = useState<DeviceAccessResult | ''>('');
  const rows = useMemo(() => (filter ? accesses.filter((a) => a.resultado === filter) : accesses), [accesses, filter]);
  const failed = accesses.filter((a) => a.resultado === 'Fallido').length;

  const columns: Column<DeviceAccess>[] = [
    {
      id: 'fecha',
      header: 'Fecha / hora',
      sortValue: (a) => a.fecha,
      cell: (a) => <span className="tabular whitespace-nowrap">{formatDateTime(a.fecha)}</span>,
    },
    { id: 'ip', header: 'IP', cell: (a) => <span className="font-mono text-xs">{a.ip}</span> },
    {
      id: 'evento',
      header: 'Evento',
      cell: (a) => (
        <span className="flex flex-col">
          <span>{a.evento}</span>
          {a.detalle && <span className="text-xs text-ink-muted">{a.detalle}</span>}
        </span>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      sortValue: (a) => a.resultado,
      cell: (a) => <Badge tone={a.resultado === 'Éxito' ? 'success' : 'danger'}>{a.resultado}</Badge>,
    },
  ];

  return (
    <Card title="Histórico de accesos" subtitle={loading ? 'Cargando…' : `${accesses.length} eventos · ${failed} fallidos`}>
      {error ? (
        <ErrorState variant="banner" title="No se pudo cargar el histórico" message={error} onRetry={onRetry} />
      ) : (
        <div className="flex flex-col gap-3">
          <SegmentedFilter<DeviceAccessResult | ''>
            label="Filtrar por resultado"
            value={filter}
            onChange={setFilter}
            options={[
              { value: '', label: 'Todos', count: accesses.length },
              { value: 'Éxito', label: 'Éxito', count: accesses.length - failed },
              { value: 'Fallido', label: 'Fallido', count: failed },
            ]}
          />
          <Table
            caption="Histórico de accesos del dispositivo"
            columns={columns}
            rows={rows}
            getRowId={(a) => a.id}
            loading={loading}
            pageSize={10}
            pageSizeOptions={[10, 25, 50]}
            resetPageKey={filter}
            initialSort={{ columnId: 'fecha', direction: 'desc' }}
            dense
            minWidth="640px"
            stickyFirstColumn
            emptyState={
              accesses.length === 0 ? (
                <EmptyState
                  icon={History}
                  title="Sin accesos todavía"
                  description="Se registrarán aquí cuando el dispositivo se vincule y sincronice por primera vez."
                />
              ) : (
                <EmptyState
                  icon={CheckCircle2}
                  title="Sin accesos fallidos"
                  description="Todos los accesos de este periodo fueron correctos."
                />
              )
            }
          />
        </div>
      )}
    </Card>
  );
}
