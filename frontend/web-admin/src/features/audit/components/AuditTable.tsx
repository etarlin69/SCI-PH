import type { ReactNode } from 'react';
import type { AuditEvent } from '../../../types';
import { Table, type Column } from '../../../components/shared';
import { formatDMY } from '../../../utils/dates';
import { AUDIT_PAGE_SIZES, formatAuditTimestamp } from '../auditFilters';
import { AuditActionTag } from './AuditActionTag';
import { AuditResultBadge } from './AuditResultBadge';

export interface AuditTableProps {
  events: AuditEvent[];
  loading: boolean;
  onOpen: (event: AuditEvent) => void;
  emptyState: ReactNode;
  resetPageKey: unknown;
  compact?: boolean;
}

/** Tabla del registro de auditoría (solo lectura): fecha/hora, usuario y rol, acción, recurso, resultado e IP. */
export function AuditTable({ events, loading, onOpen, emptyState, resetPageKey, compact = false }: AuditTableProps) {
  const columns: Column<AuditEvent>[] = [
    {
      id: 'ts',
      header: 'Fecha / hora',
      width: compact ? '96px' : '150px',
      sortValue: (e) => e.timestamp,
      cell: (e) =>
        compact ? (
          <span className="flex flex-col font-mono text-xs tabular-nums text-ink-2">
            <span>{formatDMY(e.timestamp.slice(0, 10))}</span>
            <span className="text-ink-muted">{e.timestamp.slice(11, 16)}</span>
          </span>
        ) : (
          <span className="whitespace-nowrap font-mono text-xs tabular-nums text-ink-2">{formatAuditTimestamp(e.timestamp)}</span>
        ),
    },
    {
      id: 'usuario',
      header: 'Usuario',
      sortValue: (e) => e.usuario,
      cell: (e) => (
        <span className="flex min-w-0 flex-col">
          <span className="truncate font-medium text-ink">{e.usuario}</span>
          <span className="truncate text-xs text-ink-muted">{e.rol}</span>
        </span>
      ),
    },
    { id: 'accion', header: 'Acción', sortValue: (e) => e.accion, cell: (e) => <AuditActionTag action={e.accion} /> },
    {
      id: 'recurso',
      header: 'Recurso',
      sortValue: (e) => e.recurso,
      cell: (e) => (
        <span className="block max-w-[260px] truncate font-mono text-xs text-ink-2" title={e.recurso}>
          {e.recurso}
        </span>
      ),
    },
    {
      id: 'resultado',
      header: 'Resultado',
      sortValue: (e) => e.resultado,
      cell: (e) => <AuditResultBadge result={e.resultado} error={e.error} />,
    },
    {
      id: 'ip',
      header: 'IP',
      sortValue: (e) => e.ip,
      cell: (e) => <span className="whitespace-nowrap font-mono text-xs text-ink-3">{e.ip}</span>,
    },
    {
      id: 'detalles',
      header: 'Detalles',
      width: '80px',
      cell: (e) => (
        <button
          type="button"
          onClick={() => onOpen(e)}
          aria-label={`Ver detalles del evento ${e.id}`}
          className="h-8 rounded-md px-2.5 text-[13px] font-semibold text-primary-600 hover:bg-surface-hover focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
        >
          Ver
        </button>
      ),
    },
  ];

  return (
    <Table
      columns={columns}
      rows={events}
      getRowId={(e) => e.id}
      caption="Eventos de auditoría"
      loading={loading}
      emptyState={emptyState}
      pageSize={AUDIT_PAGE_SIZES[0]}
      pageSizeOptions={AUDIT_PAGE_SIZES}
      initialSort={{ columnId: 'ts', direction: 'desc' }}
      resetPageKey={resetPageKey}
      onRowClick={onOpen}
      stickyFirstColumn
      minWidth="920px"
    />
  );
}
