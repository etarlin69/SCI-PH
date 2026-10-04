import type { ReactNode } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Pencil, ShieldCheck, Trash2 } from 'lucide-react';
import { APPLICABLE_PERMISSIONS } from '../../../data/mock';
import type { Role } from '../../../types';
import { RowActions, Table, type Column, type RowAction } from '../../../components/shared';
import { formatDMY } from '../../../utils/dates';
import { countPermissions } from '../permissions';
import { RoleTypeBadge } from './RoleTypeBadge';

export interface RolesTableProps {
  roles: Role[];
  loading: boolean;
  emptyState: ReactNode;
  canEdit: boolean;
  canDelete: boolean;
  onEdit: (role: Role) => void;
  onDelete: (role: Role) => void;
  resetPageKey?: unknown;
}

/** Lista de roles con nº de usuarios y permisos concedidos (de 35). */
export function RolesTable({ roles, loading, emptyState, canEdit, canDelete, onEdit, onDelete, resetPageKey }: RolesTableProps) {
  const navigate = useNavigate();
  const columns: Column<Role>[] = [
    {
      id: 'nombre',
      header: 'Nombre',
      sortValue: (r) => r.nombre,
      cell: (r) => (
        <Link to={`/admin/roles/${r.id}/permisos`} className="font-semibold text-ink no-underline hover:text-primary-600 hover:underline">
          {r.nombre}
        </Link>
      ),
    },
    { id: 'desc', header: 'Descripción', cell: (r) => <span className="line-clamp-2 max-w-[420px] text-ink-3">{r.descripcion}</span> },
    {
      id: 'usuarios',
      header: 'Usuarios',
      align: 'right',
      sortValue: (r) => r.usuarios,
      cell: (r) => <span className="tabular">{r.usuarios}</span>,
    },
    {
      id: 'perms',
      header: 'Permisos concedidos',
      sortValue: (r) => countPermissions(r.permisos),
      cell: (r) => {
        const n = countPermissions(r.permisos);
        return (
          <span className="flex items-center gap-2">
            <span className="h-1.5 w-16 overflow-hidden rounded-full bg-line-soft" aria-hidden="true">
              <span className="block h-full rounded-full bg-primary-600" style={{ width: `${(n / APPLICABLE_PERMISSIONS) * 100}%` }} />
            </span>
            <span className="tabular whitespace-nowrap">
              {n} de {APPLICABLE_PERMISSIONS}
            </span>
          </span>
        );
      },
    },
    { id: 'tipo', header: 'Tipo', sortValue: (r) => r.tipo, cell: (r) => <RoleTypeBadge tipo={r.tipo} /> },
    {
      id: 'upd',
      header: 'Actualizado',
      sortValue: (r) => r.actualizadoEl,
      cell: (r) => <span className="tabular text-ink-3">{formatDMY(r.actualizadoEl)}</span>,
    },
    {
      id: 'acciones',
      header: <span className="sr-only">Acciones</span>,
      align: 'right',
      cell: (r) => {
        const system = r.tipo === 'Sistema';
        const actions: RowAction[] = [
          {
            label: system || !canEdit ? 'Ver permisos' : 'Configurar permisos',
            icon: <ShieldCheck size={15} aria-hidden="true" />,
            onSelect: () => navigate(`/admin/roles/${r.id}/permisos`),
          },
        ];
        if (canEdit)
          actions.push({
            label: 'Editar nombre y descripción',
            icon: <Pencil size={15} aria-hidden="true" />,
            onSelect: () => onEdit(r),
            disabled: system,
            disabledReason: 'Los roles del sistema son de solo lectura.',
          });
        if (canDelete)
          actions.push({
            label: 'Eliminar rol',
            tone: 'danger',
            icon: <Trash2 size={15} aria-hidden="true" />,
            onSelect: () => onDelete(r),
            disabled: system,
            disabledReason: 'Los roles del sistema no se pueden eliminar.',
          });
        return <RowActions label={`Acciones para ${r.nombre}`} actions={actions} />;
      },
    },
  ];

  return (
    <Table
      caption="Roles del portal"
      columns={columns}
      rows={roles}
      getRowId={(r) => r.id}
      loading={loading}
      emptyState={emptyState}
      resetPageKey={resetPageKey}
      minWidth="860px"
      stickyFirstColumn
    />
  );
}
