import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Eye, Pencil, PauseCircle, PlayCircle, ShieldCheck, ShieldOff, Trash2 } from 'lucide-react';
import type { User } from '../../../types';
import { Avatar, RowActions, Table, type Column, type RowAction } from '../../../components/shared';
import { formatRelative } from '../../../utils/dates';
import { UserStatusBadge } from './UserStatusBadge';

export interface UsersTableProps {
  users: User[];
  loading: boolean;
  emptyState: ReactNode;
  selfId: string;
  canUpdate: boolean;
  canDelete: boolean;
  onOpen: (user: User) => void;
  onEdit: (user: User) => void;
  onToggleStatus: (user: User) => void;
  onDelete: (user: User) => void;
  resetPageKey?: unknown;
}

/** Tabla de usuarios: 10/25/50 por página, ordenable, primera columna fija en móvil. */
export function UsersTable({
  users,
  loading,
  emptyState,
  selfId,
  canUpdate,
  canDelete,
  onOpen,
  onEdit,
  onToggleStatus,
  onDelete,
  resetPageKey,
}: UsersTableProps) {
  const columns: Column<User>[] = [
    {
      id: 'usuario',
      header: 'Usuario',
      sortValue: (u) => u.nombre,
      cell: (u) => (
        <span className="flex min-w-0 items-center gap-2.5">
          <Avatar name={u.nombre} size="sm" />
          <span className="flex min-w-0 flex-col">
            <span className="flex items-center gap-1.5">
              <Link
                to={`/admin/usuarios/${u.id}`}
                className="truncate font-semibold text-ink no-underline hover:text-primary-600 hover:underline"
              >
                {u.nombre}
              </Link>
              {u.id === selfId && <span className="rounded bg-primary-100 px-1.5 text-[11px] font-semibold text-primary-600">Tú</span>}
            </span>
            <span className="truncate text-xs text-ink-muted">{u.email}</span>
          </span>
        </span>
      ),
    },
    { id: 'rol', header: 'Rol', sortValue: (u) => u.rol, cell: (u) => u.rol },
    { id: 'estado', header: 'Estado', sortValue: (u) => u.estado, cell: (u) => <UserStatusBadge estado={u.estado} /> },
    {
      id: 'mfa',
      header: 'MFA',
      sortValue: (u) => (u.mfaActivo ? 1 : 0),
      cell: (u) =>
        u.mfaActivo ? (
          <span className="flex items-center gap-1 text-success-600">
            <ShieldCheck size={15} aria-hidden="true" /> Activo
          </span>
        ) : (
          <span className="flex items-center gap-1 text-ink-muted">
            <ShieldOff size={15} aria-hidden="true" /> Pendiente
          </span>
        ),
    },
    {
      id: 'acceso',
      header: 'Último acceso',
      sortValue: (u) => u.ultimoAcceso ?? '',
      cell: (u) => <span className="tabular whitespace-nowrap text-ink-3">{formatRelative(u.ultimoAcceso)}</span>,
    },
    {
      id: 'acciones',
      header: <span className="sr-only">Acciones</span>,
      align: 'right',
      width: '56px',
      cell: (u) => {
        const self = u.id === selfId;
        const suspended = u.estado === 'Suspendido';
        const actions: RowAction[] = [{ label: 'Ver detalles', icon: <Eye size={15} aria-hidden="true" />, onSelect: () => onOpen(u) }];
        if (canUpdate) {
          actions.push({ label: 'Editar', icon: <Pencil size={15} aria-hidden="true" />, onSelect: () => onEdit(u) });
          actions.push({
            label: suspended ? 'Reactivar' : 'Suspender',
            icon: suspended ? <PlayCircle size={15} aria-hidden="true" /> : <PauseCircle size={15} aria-hidden="true" />,
            onSelect: () => onToggleStatus(u),
            disabled: self,
            disabledReason: 'No puedes suspender tu propia cuenta',
          });
        }
        if (canDelete)
          actions.push({
            label: 'Eliminar',
            tone: 'danger',
            icon: <Trash2 size={15} aria-hidden="true" />,
            onSelect: () => onDelete(u),
            disabled: self,
            disabledReason: 'No puedes eliminar tu propia cuenta',
          });
        return <RowActions label={`Acciones para ${u.nombre}`} actions={actions} />;
      },
    },
  ];

  return (
    <Table
      caption="Usuarios del portal"
      columns={columns}
      rows={users}
      getRowId={(u) => u.id}
      loading={loading}
      emptyState={emptyState}
      pageSize={10}
      pageSizeOptions={[10, 25, 50]}
      initialSort={{ columnId: 'usuario', direction: 'asc' }}
      resetPageKey={resetPageKey}
      onRowClick={onOpen}
      minWidth="760px"
      stickyFirstColumn
      rowClassName={(u) => (u.estado === 'Suspendido' ? 'text-ink-muted' : undefined)}
    />
  );
}
