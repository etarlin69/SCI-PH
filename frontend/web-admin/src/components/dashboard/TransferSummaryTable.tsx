import type { TransferSummary } from '../../types';
import { formatNumber } from '../../utils/formatters';
import { Card, Table, type Column } from '../shared';

export interface TransferSummaryTableProps {
    rows: TransferSummary[];
    loading?: boolean;
}

// Vista agregada para administración: cuenta transferencias por destino y estado, sin datos de pacientes.
export function TransferSummaryTable({ rows, loading = false }: TransferSummaryTableProps) {
    const count = (value: number) => <span className="tabular">{formatNumber(value)}</span>;
    const columns: Column<TransferSummary>[] = [
        {
            id: 'destino',
            header: 'Destino',
            cell: (r) => <span className="block max-w-[220px] truncate font-medium">{r.destino}</span>,
            sortValue: (r) => r.destino,
        },
        { id: 'recibidas', header: 'Recibidas', align: 'right', cell: (r) => count(r.recibidas), sortValue: (r) => r.recibidas },
        { id: 'enTransito', header: 'En tránsito', align: 'right', cell: (r) => count(r.enTransito), sortValue: (r) => r.enTransito },
        { id: 'pendientes', header: 'Pendientes', align: 'right', cell: (r) => count(r.pendientes), sortValue: (r) => r.pendientes },
        { id: 'total', header: 'Total', align: 'right', cell: (r) => <strong>{count(r.total)}</strong>, sortValue: (r) => r.total },
    ];

    return (
        <Card title="Transferencias por destino" subtitle="Conteo agregado del rango seleccionado">
            <Table
                columns={columns}
                rows={rows}
                getRowId={(r) => r.destino}
                caption="Transferencias por destino y estado"
                initialSort={{ columnId: 'total', direction: 'desc' }}
                loading={loading}
                dense
                emptyState={<span className="font-semibold text-ink">Sin transferencias en este rango y hospitales</span>}
            />
        </Card>
    );
}
