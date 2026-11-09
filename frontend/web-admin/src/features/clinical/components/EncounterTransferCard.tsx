import { useState, type FormEvent } from 'react';
import { Permissions, useAuth } from '../../../auth';
import { Button, Card, ConfirmDialog, EmptyState, Input, Select, Textarea } from '../../../components/shared';
import { clinicalApi } from '../api';
import { useClinicalFeedback } from '../hooks/useClinicalFeedback';
import { useClinicalQuery } from '../hooks/useClinicalQuery';
import type { ClinicalEncounterSummary, ClinicalTransfer } from '../types/clinical.types';
import { canBeTransferred, formatDateTimeUtc, isEncounterOpen, startsCare } from '../utils/clinicalLabels';
import { TransferStatusBadge } from './ClinicalBadges';
import { QueryView } from './QueryView';
import { SimulatedBadge } from './SimulatedBadge';

const ACTIVE: ClinicalTransfer['status'][] = ['Requested', 'InTransit'];

type PendingAction = { kind: 'dispatch' | 'cancel'; transfer: ClinicalTransfer };

export interface EncounterTransferCardProps {
    encounter: ClinicalEncounterSummary;
    /** El despacho y la cancelación cambian el estado de la atención: la página la recarga. */
    onEncounterChanged: () => void;
}

/**
 * Traslado saliente de una atención (clínica de origen): solicitar a otra clínica, despachar y cancelar, más el historial
 * de traslados de la atención. Todo se lee del backend, así que el estado se restaura al recargar. Leer requiere
 * Transfers.Read; las acciones solo existen con Transfers.Manage y el backend sigue siendo la autoridad (403/409).
 */
export function EncounterTransferCard({ encounter, onEncounterChanged }: EncounterTransferCardProps) {
    const canManage = useAuth().can(Permissions.Transfers.Manage);
    const feedback = useClinicalFeedback('transfers');
    const transfers = useClinicalQuery(`encounter-transfers:${encounter.id}`, (signal) => clinicalApi.listEncounterTransfers(encounter.id, signal));
    const [pending, setPending] = useState<PendingAction | null>(null);

    const run = async () => {
        if (!pending) return;
        const { kind, transfer } = pending;
        try {
            if (kind === 'dispatch') await clinicalApi.dispatchTransfer(transfer.id);
            else await clinicalApi.cancelTransfer(transfer.id);
            feedback.success(kind === 'dispatch' ? 'traslado despachado' : 'traslado cancelado');
            setPending(null);
            // La tarjeta recarga sus traslados ella misma: no depende de que la página la vuelva a montar al recargar la
            // atención (con una respuesta rápida no se llega a mostrar el estado de carga y no se remonta).
            transfers.reload();
            onEncounterChanged();
        } catch (error) {
            feedback.failure(kind === 'dispatch' ? 'No se pudo despachar el traslado.' : 'No se pudo cancelar el traslado.', error);
            setPending(null);
            // Otro usuario pudo cambiar el traslado y la atención (409): se releen ambos.
            transfers.reload();
            onEncounterChanged();
        }
    };

    return (
        <Card title="Traslado" subtitle="Traslado de esta atención a otra clínica." actions={<SimulatedBadge feature="transfers" />}>
            <QueryView state={transfers.state} onRetry={transfers.reload} errorTitle="No se pudieron cargar los traslados de la atención">
                {(list) => {
                    const active = list.find((t) => ACTIVE.includes(t.status));
                    const history = list.filter((t) => t !== active);
                    return (
                        <div className="flex flex-col gap-4">
                            {active ? (
                                <ActiveTransfer transfer={active} canManage={canManage} onAction={(kind) => setPending({ kind, transfer: active })} />
                            ) : (
                                <NoActiveTransfer encounter={encounter} canManage={canManage} onRequested={transfers.reload} />
                            )}
                            {history.length > 0 && <TransferHistory transfers={history} />}
                        </div>
                    );
                }}
            </QueryView>
            <ConfirmDialog
                open={pending?.kind === 'dispatch'}
                title="Despachar traslado"
                message="El paciente sale hacia la clínica de destino. La atención quedará en traslado y no admitirá registros hasta que la reciban."
                confirmLabel="Despachar"
                tone="primary"
                onConfirm={run}
                onCancel={() => setPending(null)}
            />
            <ConfirmDialog
                open={pending?.kind === 'cancel'}
                title="Cancelar traslado"
                message={
                    pending?.transfer.status === 'InTransit'
                        ? 'El traslado en camino se cancelará y la atención volverá a estar en curso en este hospital.'
                        : 'La solicitud de traslado se cancelará. Podrás solicitar otro más adelante.'
                }
                confirmLabel="Cancelar traslado"
                cancelLabel="Volver"
                tone="danger"
                onConfirm={run}
                onCancel={() => setPending(null)}
            />
        </Card>
    );
}

function ActiveTransfer({
    transfer,
    canManage,
    onAction,
}: {
    transfer: ClinicalTransfer;
    canManage: boolean;
    onAction: (kind: 'dispatch' | 'cancel') => void;
}) {
    const inTransit = transfer.status === 'InTransit';
    return (
        <section aria-label="Traslado activo" className="flex flex-col gap-3 rounded-lg border border-line p-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="m-0 text-sm font-semibold text-ink">
                    {inTransit ? 'En traslado' : 'Traslado solicitado'} a {transfer.destinationClinic.name}
                </p>
                <TransferStatusBadge status={transfer.status} />
            </div>
            <dl className="m-0 grid gap-1 text-[13px] text-ink-3 sm:grid-cols-2">
                <div>
                    <dt className="inline text-ink-muted">Solicitado: </dt>
                    <dd className="inline m-0">{formatDateTimeUtc(transfer.requestedAtUtc)}</dd>
                </div>
                {transfer.sentAtUtc && (
                    <div>
                        <dt className="inline text-ink-muted">Salida: </dt>
                        <dd className="inline m-0">{formatDateTimeUtc(transfer.sentAtUtc)}</dd>
                    </div>
                )}
                {transfer.reason && (
                    <div className="sm:col-span-2">
                        <dt className="inline text-ink-muted">Motivo: </dt>
                        <dd className="inline m-0">{transfer.reason}</dd>
                    </div>
                )}
                {transfer.notes && (
                    <div className="sm:col-span-2">
                        <dt className="inline text-ink-muted">Notas: </dt>
                        <dd className="inline m-0 whitespace-pre-wrap">{transfer.notes}</dd>
                    </div>
                )}
            </dl>
            {inTransit && (
                <p className="m-0 text-xs text-ink-muted">La clínica de destino registrará la llegada; hasta entonces la atención no admite registros.</p>
            )}
            {canManage && (
                <div className="flex flex-wrap gap-2">
                    {!inTransit && <Button onClick={() => onAction('dispatch')}>Despachar</Button>}
                    <Button variant="secondary" onClick={() => onAction('cancel')}>
                        Cancelar traslado
                    </Button>
                </div>
            )}
        </section>
    );
}

function NoActiveTransfer({
    encounter,
    canManage,
    onRequested,
}: {
    encounter: ClinicalEncounterSummary;
    canManage: boolean;
    onRequested: () => void;
}) {
    if (!isEncounterOpen(encounter.status)) return <EmptyState title="Sin traslado activo" description="La atención está cerrada." />;
    if (!canBeTransferred(encounter.status)) {
        return (
            <EmptyState
                title="Sin traslado activo"
                description={
                    startsCare(encounter.status)
                        ? 'Registra primero la atención en este hospital (triage, signo vital, nota, diagnóstico o medicación) para poder solicitar un traslado.'
                        : 'La atención no puede trasladarse en su estado actual.'
                }
            />
        );
    }
    if (!canManage) return <EmptyState title="Sin traslado activo" />;
    return <RequestTransferForm encounter={encounter} onRequested={onRequested} />;
}

function RequestTransferForm({ encounter, onRequested }: { encounter: ClinicalEncounterSummary; onRequested: () => void }) {
    const feedback = useClinicalFeedback('transfers');
    const destinations = useClinicalQuery(`transfer-destinations:${encounter.clinicId ?? 'none'}`, (signal) =>
        clinicalApi.listTransferDestinations(encounter.clinicId, signal),
    );
    const [destinationId, setDestinationId] = useState('');
    const [reason, setReason] = useState('');
    const [notes, setNotes] = useState('');
    const [saving, setSaving] = useState(false);

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        if (saving || !destinationId) return;
        setSaving(true);
        try {
            await clinicalApi.requestTransfer(encounter.id, {
                destinationClinicId: destinationId,
                reason: reason.trim() || null,
                notes: notes.trim() || null,
            });
            feedback.success('traslado solicitado');
            setDestinationId('');
            setReason('');
            setNotes('');
            onRequested();
        } catch (error) {
            feedback.failure('No se pudo solicitar el traslado.', error);
        } finally {
            setSaving(false);
        }
    };

    return (
        <form onSubmit={submit} className="flex flex-col gap-3" aria-label="Solicitar traslado">
            <h3 className="m-0 text-sm font-semibold text-ink">Solicitar traslado</h3>
            <QueryView
                state={destinations.state}
                onRetry={destinations.reload}
                errorTitle="No se pudieron cargar las clínicas de destino"
                loadingLabel="Cargando clínicas de destino…"
                isEmpty={(list) => list.length === 0}
                empty={<EmptyState title="No hay otras clínicas activas a las que trasladar" />}
            >
                {(list) => (
                    <>
                        <Select
                            label="Clínica de destino"
                            value={destinationId}
                            onChange={(e) => setDestinationId(e.target.value)}
                            options={list.map((d) => ({ value: d.id, label: `${d.name} · ${d.municipality}, ${d.province}` }))}
                            placeholder="Selecciona…"
                            required
                        />
                        <Input label="Motivo (opcional)" value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} />
                        <Textarea label="Notas (opcional)" value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} maxLength={4000} />
                        <Button type="submit" loading={saving} loadingText="Solicitando…" disabled={!destinationId} className="w-full sm:w-fit">
                            Solicitar traslado
                        </Button>
                    </>
                )}
            </QueryView>
        </form>
    );
}

function TransferHistory({ transfers }: { transfers: ClinicalTransfer[] }) {
    return (
        <section aria-label="Historial de traslados" className="flex flex-col gap-2 border-t border-line-soft pt-3">
            <h3 className="m-0 text-sm font-semibold text-ink">Historial de traslados</h3>
            <ul className="m-0 flex list-none flex-col gap-2 p-0">
                {transfers.map((t) => (
                    <li key={t.id} className="flex flex-col gap-1 rounded-lg border border-line-soft p-3">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                            <span className="text-sm text-ink">
                                {t.originClinic?.name ?? 'Atención prehospitalaria'} → {t.destinationClinic.name}
                            </span>
                            <TransferStatusBadge status={t.status} />
                        </div>
                        <span className="text-xs text-ink-muted">
                            Solicitado {formatDateTimeUtc(t.requestedAtUtc)}
                            {t.sentAtUtc && <> · Salida {formatDateTimeUtc(t.sentAtUtc)}</>}
                            {t.receivedAtUtc && <> · Recibido {formatDateTimeUtc(t.receivedAtUtc)}</>}
                            {t.cancelledAtUtc && <> · Cancelado {formatDateTimeUtc(t.cancelledAtUtc)}</>}
                        </span>
                        {t.reason && <span className="text-[13px] text-ink-3">{t.reason}</span>}
                    </li>
                ))}
            </ul>
        </section>
    );
}
