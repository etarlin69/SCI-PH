import { AlertTriangle, BadgeCheck, Plus, RefreshCw } from 'lucide-react';
import type { License } from '../../../types';
import { Badge, Button, EmptyState, ErrorState, LICENSE_STATUS_TONE, Table, type Column } from '../../../components/shared';
import { licenseStatus } from '../../../services';
import { formatDMY } from '../../../utils/dates';

export interface LicensesTabProps {
  licenses: License[];
  loading: boolean;
  error: string | null;
  onRetry: () => void;
  canEdit: boolean;
  onAdd: () => void;
  onRenew: (license: License) => void;
}

function dueText(l: License): string {
  const { status, days } = licenseStatus(l.vence);
  if (!l.vence || days === null) return 'No vence';
  if (status === 'Vencida') return `Venció el ${formatDMY(l.vence)} (hace ${-days} días)`;
  return `${formatDMY(l.vence)} · en ${days} días`;
}

/** Pestaña "Licencias": vigencia calculada (Vigente / Por vencer ≤ 30 días / Vencida) y renovación. */
export function LicensesTab({ licenses, loading, error, onRetry, canEdit, onAdd, onRenew }: LicensesTabProps) {
  if (error) return <ErrorState variant="banner" title="No se pudieron cargar las licencias" message={error} onRetry={onRetry} />;
  const expired = licenses.filter((l) => licenseStatus(l.vence).status === 'Vencida');

  const columns: Column<License>[] = [
    { id: 'tipo', header: 'Licencia', sortValue: (l) => l.tipo, cell: (l) => <span className="font-medium text-ink">{l.tipo}</span> },
    { id: 'num', header: 'Número', cell: (l) => <span className="font-mono text-xs">{l.numero}</span> },
    { id: 'vence', header: 'Vencimiento', sortValue: (l) => l.vence ?? '9999', cell: (l) => <span className="tabular">{dueText(l)}</span> },
    {
      id: 'estado',
      header: 'Estado',
      cell: (l) => {
        const s = licenseStatus(l.vence).status;
        return <Badge tone={LICENSE_STATUS_TONE[s]}>{s}</Badge>;
      },
    },
    {
      id: 'acc',
      header: <span className="sr-only">Acciones</span>,
      align: 'right',
      cell: (l) =>
        canEdit && licenseStatus(l.vence).status !== 'Vigente' ? (
          <Button size="sm" variant="ghost" leftIcon={<RefreshCw size={14} aria-hidden="true" />} onClick={() => onRenew(l)}>
            Registrar renovación
          </Button>
        ) : null,
    },
  ];

  return (
    <div className="flex flex-col gap-3">
      {expired.length > 0 && (
        <div
          role="alert"
          className="flex gap-2.5 rounded-lg border border-danger-600/30 bg-danger-50 px-3.5 py-3 text-[13px] text-danger-text"
        >
          <AlertTriangle size={16} className="mt-0.5 shrink-0" aria-hidden="true" />
          <span>
            <strong>
              {expired.map((l) => l.tipo.split(' · ')[0]).join(', ')} vencida{expired.length > 1 ? 's' : ''}.
            </strong>{' '}
            Registra la renovación para que pueda seguir asignado a soporte vital avanzado.
          </span>
        </div>
      )}
      {canEdit && (
        <div className="flex justify-end">
          <Button size="sm" variant="secondary" leftIcon={<Plus size={15} aria-hidden="true" />} onClick={onAdd}>
            Añadir licencia
          </Button>
        </div>
      )}
      <Table
        caption="Licencias y certificaciones"
        columns={columns}
        rows={licenses}
        getRowId={(l) => l.id}
        loading={loading}
        minWidth="720px"
        stickyFirstColumn
        emptyState={
          <EmptyState
            icon={BadgeCheck}
            title="Sin licencias registradas"
            description="Añade exequátur y certificaciones (ACLS, BLS, PHTLS…)."
          />
        }
      />
    </div>
  );
}
