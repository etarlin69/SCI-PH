import { useState } from 'react';
import { Permissions, useAuth } from '../../../auth';
import { Link } from 'react-router-dom';
import { Button, Card, ConfirmDialog, EmptyState } from '../../../components/shared';
import { clinicalApi } from '../api';
import { useClinicalFeedback } from '../hooks/useClinicalFeedback';
import { useClinicalQuery } from '../hooks/useClinicalQuery';
import type { ClinicalTransfer } from '../types/clinical.types';
import { formatDateTimeUtc } from '../utils/clinicalLabels';
import { TransferStatusBadge, TriagePriorityBadge } from './ClinicalBadges';
import { EncounterSubject } from './EncounterSubject';
import { QueryView } from './QueryView';
import { SimulatedBadge } from './SimulatedBadge';

/**
 * Traslados con destino a este hospital (lado receptor). El registro y el despacho del traslado los hace la app
 * paramédica (Rol 6); aquí solo se consulta y se marca la llegada.
 */
export function IncomingTransfersPanel() {
    const { state, reload } = useClinicalQuery('incoming-transfers', (signal) => clinicalApi.listIncomingTransfers(signal));
    const feedback = useClinicalFeedback('transfers');
    const canManage = useAuth().can(Permissions.Transfers.Manage);
    const [pending, setPending] = useState<ClinicalTransfer | null>(null);
    const [received, setReceived] = useState<ClinicalTransfer | null>(null);

    const receive = async () => {
        if (!pending) return;
        try {
            await clinicalApi.receiveTransfer(pending.id);
            feedback.success('traslado recibido');
            setReceived(pending);
            setPending(null);
            reload();
        } catch (error) {
            feedback.failure('No se pudo registrar la recepción.', error);
            setPending(null);
        }
    };

    return (
        <Card
            title="Traslados entrantes"
            subtitle="Ambulancias y traslados con destino a este hospital."
            actions={<SimulatedBadge feature="transfers" />}
        >
            {received && (
                <p role="status" className="m-0 mb-3 rounded-lg bg-success-50 px-3 py-2 text-sm text-success-700">
                    Traslado recibido.{' '}
                    <Link to={`/hospital/atenciones/${received.encounterId}`} className="font-semibold text-success-700">
                        Abrir la atención
                    </Link>
                </p>
            )}
            <QueryView
                state={state}
                onRetry={reload}
                errorTitle="No se pudieron cargar los traslados."
                isEmpty={(transfers) => transfers.length === 0}
                empty={<EmptyState title="No hay traslados en camino" />}
            >
                {(transfers) => (
                    <ul className="m-0 flex list-none flex-col gap-3 p-0">
                        {transfers.map((t) => (
                            <li key={t.id} className="flex flex-col gap-3 rounded-lg border border-line p-3 sm:flex-row sm:items-center sm:justify-between">
                                <div className="flex min-w-0 flex-col gap-1.5">
                                    <EncounterSubject patient={t.patient} temporaryPatientCode={t.temporaryPatientCode} />
                                    <div className="flex flex-wrap items-center gap-2">
                                        <TriagePriorityBadge priority={t.latestTriagePriority} />
                                        <TransferStatusBadge status={t.status} />
                                    </div>
                                    <p className="m-0 text-xs text-ink-muted">
                                        Origen: {t.originClinic?.name ?? 'Atención prehospitalaria'}
                                        {t.ambulance && <> · Ambulancia {t.ambulance.code}</>}
                                        {' · '}
                                        {t.sentAtUtc ? `Salida ${formatDateTimeUtc(t.sentAtUtc)}` : `Solicitado ${formatDateTimeUtc(t.requestedAtUtc)}`}
                                    </p>
                                    {t.reason && <p className="m-0 text-[13px] text-ink-3">{t.reason}</p>}
                                </div>
                                <Button
                                    variant="secondary"
                                    size="sm"
                                    className="shrink-0"
                                    disabled={t.status !== 'InTransit' || !canManage}
                                    title={t.status !== 'InTransit' ? 'Solo se puede recibir un traslado en camino.' : undefined}
                                    onClick={() => setPending(t)}
                                >
                                    Registrar llegada
                                </Button>
                            </li>
                        ))}
                    </ul>
                )}
            </QueryView>
            <ConfirmDialog
                open={pending !== null}
                title="Registrar llegada del traslado"
                message="Se marcará el traslado como recibido por este hospital y la atención pasará a estar a su cargo."
                confirmLabel="Registrar llegada"
                tone="primary"
                onConfirm={receive}
                onCancel={() => setPending(null)}
            />
        </Card>
    );
}
