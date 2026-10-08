import type { ReactNode } from 'react';
import { Pencil, PowerOff, Trash2 } from 'lucide-react';
import type { AmbulanceStatus, CatalogItemMap, CatalogKind, CatalogStatus } from '../../../types';
import { RowActions, Table, type Column, type RowAction } from '../../../components/shared';
import { CATALOG_OFF_STATUS } from '../../../services';
import { cn } from '../../../utils/cn';
import { fieldValue } from '../helpers';
import type { CatalogConfig } from '../catalogConfig';
import { CatalogStatusBadge } from './CatalogStatusBadge';

export interface CatalogTableProps<K extends CatalogKind> {
  config: CatalogConfig<K>;
  items: CatalogItemMap[K][];
  loading: boolean;
  emptyState: ReactNode;
  canUpdate: boolean;
  canDelete: boolean;
  onEdit: (item: CatalogItemMap[K]) => void;
  onDeactivate: (item: CatalogItemMap[K]) => void;
  onDelete: (item: CatalogItemMap[K]) => void;
  resetPageKey?: unknown;
}

/** Tabla genérica de un catálogo: columnas propias + Estado + Uso + Acciones. */
export function CatalogTable<K extends CatalogKind>({
  config,
  items,
  loading,
  emptyState,
  canUpdate,
  canDelete,
  onEdit,
  onDeactivate,
  onDelete,
  resetPageKey,
}: CatalogTableProps<K>) {
  const off = CATALOG_OFF_STATUS[config.kind] as string;
  const columns: Column<CatalogItemMap[K]>[] = [
    ...config.columns.map<Column<CatalogItemMap[K]>>((c) => ({
      id: c.field,
      header: c.header,
      width: c.width,
      sortValue: (i) => fieldValue(i, c.field),
      cell: (i) => (
        <span className={cn(c.mono && 'font-mono text-xs', c.strong && 'font-semibold text-ink')}>{fieldValue(i, c.field)}</span>
      ),
    })),
    {
      id: 'estado',
      header: 'Estado',
      sortValue: (i) => fieldValue(i, 'estado'),
      cell: (i) => <CatalogStatusBadge estado={fieldValue(i, 'estado') as CatalogStatus | AmbulanceStatus} />,
    },
    {
      id: 'uso',
      header: config.usageHeader,
      align: 'right',
      sortValue: (i) => i.uso,
      cell: (i) => (
        <span className="tabular whitespace-nowrap text-ink-3">
          {i.uso}
          {config.usageUnit}
        </span>
      ),
    },
    {
      id: 'acciones',
      header: <span className="sr-only">Acciones</span>,
      align: 'right',
      width: '56px',
      cell: (i) => {
        const actions: RowAction[] = [];
        if (canUpdate) {
          actions.push({ label: 'Editar', icon: <Pencil size={15} aria-hidden="true" />, onSelect: () => onEdit(i) });
          if (fieldValue(i, 'estado') !== off)
            actions.push({
              label: off === 'Mantenimiento' ? 'Pasar a mantenimiento' : 'Desactivar',
              icon: <PowerOff size={15} aria-hidden="true" />,
              onSelect: () => onDeactivate(i),
            });
        }
        if (canDelete)
          actions.push({ label: 'Eliminar', tone: 'danger', icon: <Trash2 size={15} aria-hidden="true" />, onSelect: () => onDelete(i) });
        return actions.length ? <RowActions label={`Acciones para ${config.itemName(i)}`} actions={actions} /> : null;
      },
    },
  ];

  return (
    <Table
      caption={`Catálogo de ${config.label.toLowerCase()}`}
      columns={columns}
      rows={items}
      getRowId={(i) => i.id}
      loading={loading}
      emptyState={emptyState}
      pageSize={10}
      pageSizeOptions={[10, 25, 50]}
      resetPageKey={resetPageKey}
      minWidth={config.minWidth}
      stickyFirstColumn
      rowClassName={(i) => (fieldValue(i, 'estado') === off ? 'text-ink-muted' : undefined)}
    />
  );
}
