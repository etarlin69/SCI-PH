import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Eye, RefreshCw, ShieldOff, Trash2 } from 'lucide-react';
import type { Device } from '../../../types';
import { RowActions, Table, type Column, type RowAction } from '../../../components/shared';
import { DEVICE_STATUS_ORDER, formatDateTime, relativeTime, rotateBlockedReason } from '../helpers';
import { DeviceStatusBadge } from './DeviceStatusBadge';
import { DeviceTypeIcon } from './DeviceTypeIcon';

export interface DevicesTableProps {
  devices: Device[];
  loading: boolean;
  emptyState: ReactNode;
  canUpdate: boolean;
  canDelete: boolean;
  onOpen: (device: Device) => void;
  onRotate: (device: Device) => void;
  onRevoke: (device: Device) => void;
  onDelete: (device: Device) => void;
  resetPageKey?: unknown;
}

/** Inventario de dispositivos (10/25/50 por página; primera columna fija en móvil). */
export function DevicesTable({
  devices,
  loading,
  emptyState,
  canUpdate,
  canDelete,
  onOpen,
  onRotate,
  onRevoke,
  onDelete,
  resetPageKey,
}: DevicesTableProps) {
  const columns: Column<Device>[] = [
    {
      id: 'nombre',
      header: 'Dispositivo',
      sortValue: (d) => d.nombre,
      cell: (d) => (
        <span className="flex min-w-0 items-center gap-2.5">
          <DeviceTypeIcon tipo={d.tipo} boxed />
          <span className="flex min-w-0 flex-col">
            <Link
              to={`/admin/dispositivos/${d.id}`}
              className="truncate font-semibold text-ink no-underline hover:text-primary-600 hover:underline"
            >
              {d.nombre}
            </Link>
            <span className="truncate text-xs text-ink-muted">
              <span className="font-mono">{d.id}</span> · {d.asignadoA}
            </span>
          </span>
        </span>
      ),
    },
    { id: 'tipo', header: 'Tipo', sortValue: (d) => d.tipo, cell: (d) => d.tipo },
    {
      id: 'estado',
      header: 'Estado',
      sortValue: (d) => DEVICE_STATUS_ORDER[d.estado],
      cell: (d) => <DeviceStatusBadge estado={d.estado} />,
    },
    {
      id: 'acceso',
      header: 'Último acceso',
      sortValue: (d) => d.ultimoAcceso ?? '',
      cell: (d) =>
        d.ultimoAcceso ? (
          <span className="flex flex-col">
            <span className="tabular whitespace-nowrap">{formatDateTime(d.ultimoAcceso)}</span>
            <span className="text-xs text-ink-muted">{relativeTime(d.ultimoAcceso)}</span>
          </span>
        ) : (
          <span className="text-ink-muted">Esperando primera sincronización</span>
        ),
    },
    { id: 'ip', header: 'IP', cell: (d) => <span className="font-mono text-xs text-ink-3">{d.ultimaIp ?? '—'}</span> },
    {
      id: 'acciones',
      header: <span className="sr-only">Acciones</span>,
      align: 'right',
      width: '56px',
      cell: (d) => {
        const actions: RowAction[] = [{ label: 'Ver detalles', icon: <Eye size={15} aria-hidden="true" />, onSelect: () => onOpen(d) }];
        if (canUpdate) {
          const blocked = rotateBlockedReason(d.estado);
          actions.push({
            label: 'Rotar credencial',
            icon: <RefreshCw size={15} aria-hidden="true" />,
            onSelect: () => onRotate(d),
            disabled: !!blocked,
            disabledReason: blocked,
          });
          actions.push({
            label: 'Revocar',
            tone: 'danger',
            icon: <ShieldOff size={15} aria-hidden="true" />,
            onSelect: () => onRevoke(d),
            disabled: d.estado === 'Revocado',
            disabledReason: 'Ya está revocado',
          });
        }
        if (canDelete)
          actions.push({ label: 'Eliminar', tone: 'danger', icon: <Trash2 size={15} aria-hidden="true" />, onSelect: () => onDelete(d) });
        return <RowActions label={`Acciones para ${d.nombre}`} actions={actions} />;
      },
    },
  ];

  return (
    <Table
      caption="Dispositivos registrados"
      columns={columns}
      rows={devices}
      getRowId={(d) => d.id}
      loading={loading}
      emptyState={emptyState}
      pageSize={10}
      pageSizeOptions={[10, 25, 50]}
      resetPageKey={resetPageKey}
      onRowClick={onOpen}
      minWidth="820px"
      stickyFirstColumn
      rowClassName={(d) => (d.estado === 'Revocado' ? 'text-ink-muted' : undefined)}
    />
  );
}
