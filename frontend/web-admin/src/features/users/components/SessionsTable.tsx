import type { ReactNode } from 'react';
import { Monitor, Smartphone, Tablet } from 'lucide-react';
import type { SessionKind, UserSession } from '../../../types';
import { Badge, Table, type Column } from '../../../components/shared';
import { formatDateTimeShort, formatRelative } from '../format';

export interface SessionsTableProps {
  sessions: UserSession[];
  loading?: boolean;
  canRevoke: boolean;
  onRevoke: (session: UserSession) => void;
  emptyState?: ReactNode;
}

const KIND_LABEL: Record<SessionKind, string> = { web: 'Web', mobile: 'App móvil', device: 'Dispositivo' };

const KIND_ICON: Record<SessionKind, ReactNode> = {
  web: <Monitor size={16} aria-hidden="true" />,
  mobile: <Smartphone size={16} aria-hidden="true" />,
  device: <Tablet size={16} aria-hidden="true" />,
};

/** Sesiones activas de un usuario: dispositivo, tipo, IP/ubicación (marca "Nueva ubicación"), inicio y última actividad. */
export function SessionsTable({ sessions, loading, canRevoke, onRevoke, emptyState }: SessionsTableProps) {
  const columns: Column<UserSession>[] = [
    {
      id: 'dispositivo',
      header: 'Dispositivo',
      sortValue: (s) => s.dispositivo,
      cell: (s) => (
        <div className="flex items-center gap-2.5 py-1.5">
          <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-surface-hover text-ink-3">
            {KIND_ICON[s.tipo]}
          </span>
          <div className="flex min-w-0 flex-col">
            <span className="font-medium text-ink">{s.dispositivo}</span>
            <span className="text-xs text-ink-muted">{s.plataforma}</span>
          </div>
        </div>
      ),
    },
    {
      id: 'tipo',
      header: 'Tipo',
      sortValue: (s) => KIND_LABEL[s.tipo],
      cell: (s) => (
        <Badge tone={s.tipo === 'device' ? 'accent' : 'info'} dot={false}>
          {KIND_LABEL[s.tipo]}
        </Badge>
      ),
    },
    {
      id: 'ip',
      header: 'IP · ubicación',
      sortValue: (s) => s.ubicacion,
      cell: (s) => (
        <div className="flex flex-col">
          <span className="font-mono text-xs text-ink-2">{s.ip}</span>
          <span className="flex flex-wrap items-center gap-1.5 text-xs text-ink-muted">
            {s.ubicacion}
            {s.nuevaUbicacion && (
              <Badge tone="warning" className="px-2 py-0 text-[11px]">
                Nueva ubicación
              </Badge>
            )}
          </span>
        </div>
      ),
    },
    {
      id: 'inicio',
      header: 'Inicio',
      sortValue: (s) => s.inicio,
      cell: (s) => <span className="tabular whitespace-nowrap text-ink-3">{formatDateTimeShort(s.inicio)}</span>,
    },
    {
      id: 'ultima',
      header: 'Última actividad',
      sortValue: (s) => s.ultimaActividad,
      cell: (s) => (
        <span className="tabular whitespace-nowrap text-ink-3" title={formatDateTimeShort(s.ultimaActividad)}>
          {formatRelative(s.ultimaActividad)}
        </span>
      ),
    },
  ];

  if (canRevoke) {
    columns.push({
      id: 'acciones',
      header: <span className="sr-only">Acciones</span>,
      align: 'right',
      cell: (s) => (
        <button
          type="button"
          onClick={() => onRevoke(s)}
          aria-label={`Revocar sesión en ${s.dispositivo}`}
          className="inline-flex h-8 items-center rounded-md border border-danger-600/30 bg-white px-3 text-[13px] font-medium text-danger-text hover:bg-danger-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-danger-600"
        >
          Revocar
        </button>
      ),
    });
  }

  return (
    <Table
      caption="Sesiones activas"
      columns={columns}
      rows={sessions}
      getRowId={(s) => s.id}
      loading={loading}
      emptyState={emptyState}
      initialSort={{ columnId: 'ultima', direction: 'desc' }}
      stickyFirstColumn
      minWidth="820px"
    />
  );
}
