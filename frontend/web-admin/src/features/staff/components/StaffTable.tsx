import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Eye, Pencil, Trash2 } from 'lucide-react';
import type { StaffMember } from '../../../types';
import { Avatar, RowActions, Table, type Column, type RowAction } from '../../../components/shared';
import { StaffAccountBadge } from './StaffAccountBadge';
import { StaffStatusBadge } from './StaffStatusBadge';

export interface StaffTableProps {
  staff: StaffMember[];
  clinicName: (id: string) => string;
  loading: boolean;
  emptyState: ReactNode;
  canUpdate: boolean;
  canDelete: boolean;
  onOpen: (s: StaffMember) => void;
  onEdit: (s: StaffMember) => void;
  onDelete: (s: StaffMember) => void;
  resetPageKey?: unknown;
}

/** Tabla de personal: nombre + cuenta, puesto, clínica, departamento y estado. */
export function StaffTable({
  staff,
  clinicName,
  loading,
  emptyState,
  canUpdate,
  canDelete,
  onOpen,
  onEdit,
  onDelete,
  resetPageKey,
}: StaffTableProps) {
  const columns: Column<StaffMember>[] = [
    {
      id: 'nombre',
      header: 'Nombre',
      sortValue: (s) => s.nombre,
      cell: (s) => (
        <span className="flex min-w-0 items-center gap-2.5">
          <Avatar name={s.nombre} size="sm" />
          <span className="flex min-w-0 flex-col">
            <Link
              to={`/admin/personal/${s.id}`}
              className="truncate font-semibold text-ink no-underline hover:text-primary-600 hover:underline"
            >
              {s.nombre}
            </Link>
            <span className="truncate text-xs text-ink-muted">{s.email}</span>
            <StaffAccountBadge cuenta={s.cuenta} />
          </span>
        </span>
      ),
    },
    { id: 'puesto', header: 'Puesto', sortValue: (s) => s.puesto, cell: (s) => s.puesto },
    { id: 'clinica', header: 'Clínica', sortValue: (s) => clinicName(s.clinicId), cell: (s) => clinicName(s.clinicId) },
    { id: 'dep', header: 'Departamento', sortValue: (s) => s.departamento, cell: (s) => s.departamento },
    { id: 'estado', header: 'Estado', sortValue: (s) => s.estado, cell: (s) => <StaffStatusBadge estado={s.estado} /> },
    {
      id: 'acciones',
      header: <span className="sr-only">Acciones</span>,
      align: 'right',
      width: '56px',
      cell: (s) => {
        const actions: RowAction[] = [{ label: 'Ver ficha', icon: <Eye size={15} aria-hidden="true" />, onSelect: () => onOpen(s) }];
        if (canUpdate) actions.push({ label: 'Editar', icon: <Pencil size={15} aria-hidden="true" />, onSelect: () => onEdit(s) });
        if (canDelete)
          actions.push({ label: 'Eliminar', tone: 'danger', icon: <Trash2 size={15} aria-hidden="true" />, onSelect: () => onDelete(s) });
        return <RowActions label={`Acciones para ${s.nombre}`} actions={actions} />;
      },
    },
  ];
  return (
    <Table
      caption="Personal registrado"
      columns={columns}
      rows={staff}
      getRowId={(s) => s.id}
      loading={loading}
      emptyState={emptyState}
      pageSize={10}
      pageSizeOptions={[10, 25, 50]}
      initialSort={{ columnId: 'nombre', direction: 'asc' }}
      resetPageKey={resetPageKey}
      onRowClick={onOpen}
      minWidth="860px"
      stickyFirstColumn
    />
  );
}
