import type { ReactNode } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Ban, Building2, CheckCircle2, Eye, Pencil, Trash2 } from 'lucide-react';
import type { Clinic } from '../../../types';
import { RowActions, Table, type Column, type RowAction } from '../../../components/shared';
import { useIsMobile } from '../../../hooks/useMediaQuery';
import { ClinicStatusBadge } from './ClinicStatusBadge';

export interface ClinicsTableProps {
  rows: Clinic[];
  loading: boolean;
  emptyState: ReactNode;
  resetPageKey: unknown;
  canUpdate: boolean;
  canDelete: boolean;
  onEdit: (clinic: Clinic) => void;
  onToggleStatus: (clinic: Clinic) => void;
  onDelete: (clinic: Clinic) => void;
}

/** Tabla de clínicas: nombre y tipo, ubicación, contacto, personal, dispositivos, estado y menú ⋯. */
export function ClinicsTable({
  rows,
  loading,
  emptyState,
  resetPageKey,
  canUpdate,
  canDelete,
  onEdit,
  onToggleStatus,
  onDelete,
}: ClinicsTableProps) {
  const navigate = useNavigate();
  const isMobile = useIsMobile();

  const actionsFor = (c: Clinic): RowAction[] => {
    const list: RowAction[] = [
      { label: 'Ver detalle', icon: <Eye size={16} aria-hidden="true" />, onSelect: () => navigate(`/admin/clinicas/${c.id}`) },
    ];
    if (canUpdate) {
      list.push({ label: 'Editar', icon: <Pencil size={16} aria-hidden="true" />, onSelect: () => onEdit(c) });
      list.push(
        c.estado === 'Activa'
          ? { label: 'Desactivar', icon: <Ban size={16} aria-hidden="true" />, onSelect: () => onToggleStatus(c), tone: 'danger' }
          : { label: 'Reactivar', icon: <CheckCircle2 size={16} aria-hidden="true" />, onSelect: () => onToggleStatus(c) },
      );
    }
    if (canDelete) {
      list.push({ label: 'Eliminar', icon: <Trash2 size={16} aria-hidden="true" />, onSelect: () => onDelete(c), tone: 'danger' });
    }
    return list;
  };

  const columns: Column<Clinic>[] = [
    {
      id: 'nombre',
      header: 'Clínica',
      sortValue: (c) => c.nombre,
      cell: (c) => (
        <span className="flex min-w-0 items-center gap-3 py-1.5">
          <span
            aria-hidden="true"
            className="hidden h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-primary-100 text-primary-600 sm:flex"
          >
            <Building2 size={18} />
          </span>
          <span className="flex min-w-0 flex-col">
            <Link
              to={`/admin/clinicas/${c.id}`}
              className="truncate rounded font-semibold text-primary-600 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
            >
              {c.nombre}
            </Link>
            <span className="text-xs text-ink-muted">
              <span className="font-mono">{c.id}</span> · {c.tipo}
            </span>
          </span>
        </span>
      ),
    },
    {
      id: 'ubicacion',
      header: 'Ubicación',
      sortValue: (c) => `${c.provincia} ${c.direccion}`,
      cell: (c) => (
        <span className="flex flex-col">
          <span className="text-ink">{c.direccion}</span>
          <span className="text-xs text-ink-muted">{c.provincia}</span>
        </span>
      ),
    },
    {
      id: 'contacto',
      header: 'Contacto',
      sortValue: (c) => c.contacto,
      cell: (c) => (
        <span className="flex flex-col">
          <span className="text-ink">{c.contacto}</span>
          <a href={`tel:${c.telefono}`} className="w-fit font-mono text-xs text-ink-muted hover:text-primary-600 hover:underline">
            {c.telefono}
          </a>
        </span>
      ),
    },
    {
      id: 'personal',
      header: 'Personal',
      align: 'right',
      sortValue: (c) => c.personal,
      cell: (c) => <span className="tabular">{c.personal}</span>,
    },
    {
      id: 'dispositivos',
      header: 'Dispositivos',
      align: 'right',
      sortValue: (c) => c.dispositivos,
      cell: (c) => <span className="tabular">{c.dispositivos}</span>,
    },
    { id: 'estado', header: 'Estado', sortValue: (c) => c.estado, cell: (c) => <ClinicStatusBadge estado={c.estado} /> },
    {
      id: 'acciones',
      header: <span className="sr-only">Acciones</span>,
      align: 'right',
      width: '56px',
      cell: (c) => <RowActions label={`Acciones para ${c.nombre}`} actions={actionsFor(c)} />,
    },
  ];

  return (
    <Table
      caption="Clínicas"
      columns={columns}
      rows={rows}
      getRowId={(c) => c.id}
      loading={loading}
      emptyState={emptyState}
      initialSort={{ columnId: 'nombre', direction: 'asc' }}
      pageSize={10}
      pageSizeOptions={[10, 25, 50]}
      resetPageKey={resetPageKey}
      onRowClick={(c) => navigate(`/admin/clinicas/${c.id}`)}
      stickyFirstColumn={isMobile}
      minWidth="920px"
    />
  );
}
