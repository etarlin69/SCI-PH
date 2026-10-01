import { Link } from 'react-router-dom';
import { Eye } from 'lucide-react';
import type { RecentTransfer } from '../../types';
import { formatDM } from '../../utils/dates';
import { Badge, Card, Table, TRANSFER_STATUS_TONE, type Column } from '../shared';

export interface RecentEventsProps {
    events: RecentTransfer[];
    loading?: boolean;
    onViewDetails?: (_event: RecentTransfer) => void;
}
export function RecentEvents({ events, loading = false, onViewDetails }: RecentEventsProps) {
    const columns: Column<RecentTransfer>[] = [
        {
            id: 'paciente',
            header: 'Paciente',
            width: '124px',
            cell: (e) => <span className="block truncate font-medium">{e.paciente}</span>,
            sortValue: (e) => e.paciente,
        },
        { id: 'origen', header: 'Origen', cell: (e) => <span className="block max-w-[180px] truncate text-ink-3">{e.origen}</span> },
        { id: 'destino', header: 'Destino', cell: (e) => <span className="block max-w-[180px] truncate text-ink-3">{e.destino}</span> },
        {
            id: 'fecha',
            header: 'Fecha / hora',
            width: '104px',
            cell: (e) => <span className="tabular font-mono text-xs">{`${formatDM(e.fecha)} ${e.hora}`}</span>,
            sortValue: (e) => `${e.fecha} ${e.hora}`,
        },
        { id: 'estado', header: 'Estado', width: '124px', cell: (e) => <Badge tone={TRANSFER_STATUS_TONE[e.estado]}>{e.estado}</Badge> },
        {
            id: 'acciones',
            header: <span className="sr-only">Acciones</span>,
            width: '48px',
            cell: (e) => (
                <button
                    type="button"
                    onClick={() => onViewDetails?.(e)}
                    aria-label={`Ver detalles de la transferencia de ${e.paciente}`}
                    className="flex h-8 w-8 items-center justify-center rounded-md text-primary-600 hover:bg-surface-hover focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
                >
                    <Eye size={16} aria-hidden="true" />
                </button>
            ),
        },
    ];

    return (
        <Card
            title="Eventos recientes"
            subtitle={events.length ? `Últimas ${events.length} transferencias del rango` : 'Rango sin transferencias registradas'}
            actions={
                <Link to="/auditoria" className="shrink-0 text-[13px] font-medium text-primary-600 hover:text-primary-700">
                    Ver en auditoría
                </Link>
            }
        >
            <Table
                columns={columns}
                rows={events}
                getRowId={(e) => e.id}
                caption="Últimas transferencias"
                initialSort={{ columnId: 'fecha', direction: 'desc' }}
                loading={loading}
                dense
                emptyState={
                    <span className="flex flex-col items-center gap-1">
                        <span className="font-semibold text-ink">Sin transferencias en este rango y hospitales</span>
                        <span>El historial completo está en Auditoría.</span>
                    </span>
                }
            />
        </Card>
    );
}
