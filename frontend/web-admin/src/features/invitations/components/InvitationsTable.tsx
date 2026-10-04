import type { ReactNode } from 'react';
import { Ban, Eye, Send, Stethoscope, User } from 'lucide-react';
import type { Invitation } from '../../../types';
import { RowActions, Table, type Column, type RowAction } from '../../../components/shared';
import { canResend, canRevoke, expiryOf, expiryText, formatDateTime, parseDateTime } from '../helpers';
import { InvitationStatusBadge } from './InvitationStatusBadge';

export interface InvitationsTableProps {
  invitations: Invitation[];
  loading: boolean;
  emptyState: ReactNode;
  canManage: boolean;
  onOpen: (inv: Invitation) => void;
  onResend: (inv: Invitation) => void;
  onRevoke: (inv: Invitation) => void;
  resetPageKey?: unknown;
}

/** Tabla de invitaciones (10/25/50; email fijo al desplazar en móvil). */
export function InvitationsTable({
  invitations,
  loading,
  emptyState,
  canManage,
  onOpen,
  onResend,
  onRevoke,
  resetPageKey,
}: InvitationsTableProps) {
  const columns: Column<Invitation>[] = [
    {
      id: 'email',
      header: 'Email',
      sortValue: (i) => i.email,
      cell: (i) => (
        <span className="flex min-w-0 flex-col">
          <button
            type="button"
            onClick={() => onOpen(i)}
            className="truncate text-left font-semibold text-ink hover:text-primary-600 hover:underline"
          >
            {i.email}
          </button>
          <span className="truncate text-xs text-ink-muted">
            <span className="font-mono">{i.id}</span> · por {i.enviadaPor}
          </span>
        </span>
      ),
    },
    {
      id: 'tipo',
      header: 'Tipo',
      sortValue: (i) => i.tipo,
      cell: (i) => (
        <span className="flex flex-col">
          <span className="flex items-center gap-1.5">
            {i.tipo === 'Paciente' ? <User size={14} aria-hidden="true" /> : <Stethoscope size={14} aria-hidden="true" />}
            {i.tipo}
          </span>
          {i.tipo === 'Personal' && <span className="text-xs text-ink-muted">{[i.rol, i.departamento].filter(Boolean).join(' · ')}</span>}
        </span>
      ),
    },
    { id: 'estado', header: 'Estado', sortValue: (i) => i.estado, cell: (i) => <InvitationStatusBadge estado={i.estado} /> },
    {
      id: 'enviada',
      header: 'Enviada',
      sortValue: (i) => i.enviadaEl,
      cell: (i) => (
        <span className="flex flex-col">
          <span className="tabular whitespace-nowrap">{formatDateTime(parseDateTime(i.enviadaEl))}</span>
          {i.reenvios > 0 && (
            <span className="text-xs text-ink-muted">
              {i.reenvios} {i.reenvios === 1 ? 'reenvío' : 'reenvíos'}
            </span>
          )}
        </span>
      ),
    },
    {
      id: 'caduca',
      header: 'Caducidad',
      sortValue: (i) => expiryOf(i).getTime(),
      cell: (i) => <span className={i.estado === 'Pendiente' ? 'text-ink-3' : 'text-ink-muted'}>{expiryText(i)}</span>,
    },
    {
      id: 'acciones',
      header: <span className="sr-only">Acciones</span>,
      align: 'right',
      width: '56px',
      cell: (i) => {
        const actions: RowAction[] = [{ label: 'Ver detalle', icon: <Eye size={15} aria-hidden="true" />, onSelect: () => onOpen(i) }];
        if (canManage) {
          actions.push({
            label: 'Reenviar (genera un enlace nuevo)',
            icon: <Send size={15} aria-hidden="true" />,
            onSelect: () => onResend(i),
            disabled: !canResend(i),
            disabledReason: 'Ya aceptada: no se puede reenviar',
          });
          actions.push({
            label: 'Revocar',
            tone: 'danger',
            icon: <Ban size={15} aria-hidden="true" />,
            onSelect: () => onRevoke(i),
            disabled: !canRevoke(i),
            disabledReason: 'Solo se pueden revocar invitaciones pendientes',
          });
        }
        return <RowActions label={`Acciones para la invitación a ${i.email}`} actions={actions} />;
      },
    },
  ];

  return (
    <Table
      caption="Invitaciones enviadas"
      columns={columns}
      rows={invitations}
      getRowId={(i) => i.id}
      loading={loading}
      emptyState={emptyState}
      pageSize={10}
      pageSizeOptions={[10, 25, 50]}
      initialSort={{ columnId: 'enviada', direction: 'desc' }}
      resetPageKey={resetPageKey}
      onRowClick={onOpen}
      minWidth="820px"
      stickyFirstColumn
    />
  );
}
