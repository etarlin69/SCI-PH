import { CalendarPlus, MapPinned } from 'lucide-react';
import type { Assignment } from '../../../types';
import { Badge, Button, EmptyState, ErrorState, Table, type Column } from '../../../components/shared';
import { formatDMY } from '../../../utils/dates';

export interface AssignmentsTabProps {
  assignments: Assignment[];
  loading: boolean;
  error: string | null;
  onRetry: () => void;
  canAdd: boolean;
  onAdd: () => void;
}

/** Pestaña "Asignaciones": clínica/unidad, departamento, turno y vigencia. */
export function AssignmentsTab({ assignments, loading, error, onRetry, canAdd, onAdd }: AssignmentsTabProps) {
  const columns: Column<Assignment>[] = [
    {
      id: 'lugar',
      header: 'Clínica / unidad',
      sortValue: (a) => a.lugar,
      cell: (a) => <span className="font-medium text-ink">{a.lugar}</span>,
    },
    { id: 'dep', header: 'Departamento', cell: (a) => a.departamento },
    { id: 'turno', header: 'Turno', cell: (a) => a.turno },
    { id: 'desde', header: 'Desde', sortValue: (a) => a.desde, cell: (a) => <span className="tabular">{formatDMY(a.desde)}</span> },
    { id: 'hasta', header: 'Hasta', cell: (a) => <span className="tabular">{a.hasta ? formatDMY(a.hasta) : '—'}</span> },
    {
      id: 'estado',
      header: 'Estado',
      cell: (a) => (a.hasta ? <Badge tone="neutral">Finalizada</Badge> : <Badge tone="success">Actual</Badge>),
    },
  ];
  if (error) return <ErrorState variant="banner" title="No se pudieron cargar las asignaciones" message={error} onRetry={onRetry} />;
  return (
    <div className="flex flex-col gap-3">
      {canAdd && (
        <div className="flex justify-end">
          <Button size="sm" variant="secondary" leftIcon={<CalendarPlus size={15} aria-hidden="true" />} onClick={onAdd}>
            Nueva asignación
          </Button>
        </div>
      )}
      <Table
        caption="Asignaciones"
        columns={columns}
        rows={assignments}
        getRowId={(a) => a.id}
        loading={loading}
        minWidth="680px"
        stickyFirstColumn
        emptyState={<EmptyState icon={MapPinned} title="Sin asignaciones" description="Asígnale una clínica o unidad y un turno." />}
      />
    </div>
  );
}
