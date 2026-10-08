import { useState, type FormEvent } from 'react';
import { Permissions, useAuth } from '../../../auth';
import { Link, useParams } from 'react-router-dom';
import { Button, Card, EmptyState, Input, Select, Textarea } from '../../../components/shared';
import { cn } from '../../../utils/cn';
import { clinicalApi } from '../api';
import { EncounterStatusBadge, TriagePriorityBadge } from '../components/ClinicalBadges';
import { ClinicSelector } from '../components/ClinicSelector';
import { EncounterShell } from '../components/EncounterShell';
import { EncounterSubject } from '../components/EncounterSubject';
import { QueryView } from '../components/QueryView';
import { useClinicSelection } from '../hooks/useClinicSelection';
import { useClinicalFeedback } from '../hooks/useClinicalFeedback';
import { useClinicalQuery } from '../hooks/useClinicalQuery';
import type { ClinicalEncounterSummary, TriagePriority, VitalSignInput, VitalSignType } from '../types/clinical.types';
import {
    TRIAGE_PRIORITIES,
    VITAL_SIGNS,
    formatDateTimeUtc,
    isEncounterOpen,
    localInputToUtc,
    nowForDateTimeInput,
    vitalSignMeta,
} from '../utils/clinicalLabels';

/** /hospital/triage: cola de atenciones abiertas. /hospital/atenciones/:id/triage: formulario e historial. */
export function TriagePage() {
    const { encounterId } = useParams();
    if (!encounterId) return <TriageQueue />;

    return (
        <EncounterShell encounterId={encounterId}>
            {(encounter, reloadEncounter) => (
                <div className="grid gap-5 xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
                    <TriageForm encounter={encounter} onSaved={reloadEncounter} />
                    <TriageHistory encounter={encounter} onSaved={reloadEncounter} />
                </div>
            )}
        </EncounterShell>
    );
}

const PRIORITY_RANK: Record<TriagePriority, number> = { Immediate: 1, VeryUrgent: 2, Urgent: 3, Standard: 4, NonUrgent: 5 };

function TriageQueue() {
    const clinic = useClinicSelection();
    const { state, reload } = useClinicalQuery(clinic.ready ? `triage-queue:${clinic.queryKey}` : null, (signal) =>
        clinicalApi.listActiveEncounters(signal),
    );

    // Primero las atenciones sin triage; después por prioridad. Solo ordena la vista, no decide nada clínico.
    const sort = (list: ClinicalEncounterSummary[]) =>
        [...list].sort((a, b) => (a.latestTriagePriority ? PRIORITY_RANK[a.latestTriagePriority] : 0) - (b.latestTriagePriority ? PRIORITY_RANK[b.latestTriagePriority] : 0));

    return (
        <div className="flex flex-col gap-5">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="m-0 text-2xl font-bold text-ink">Triage</h1>
                    <p className="m-0 mt-1 text-sm text-ink-muted">Atenciones abiertas; las que aún no tienen triage aparecen primero.</p>
                </div>
                <ClinicSelector />
            </div>
            <Card padding="none">
                <QueryView
                    state={state}
                    onRetry={reload}
                    errorTitle="No se pudieron cargar las atenciones."
                    isEmpty={(list) => list.length === 0}
                    empty={
                        <div className="p-4">
                            <EmptyState title="No hay atenciones abiertas" />
                        </div>
                    }
                >
                    {(list) => (
                        <ul className="m-0 list-none divide-y divide-line-soft p-0">
                            {sort(list).map((e) => (
                                <li key={e.id}>
                                    <Link
                                        to={`/hospital/atenciones/${e.id}/triage`}
                                        className="flex flex-col gap-2 px-4 py-3 text-ink no-underline hover:bg-surface-hover focus-visible:outline focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary-600 sm:flex-row sm:items-center sm:justify-between"
                                    >
                                        <EncounterSubject patient={e.patient} temporaryPatientCode={e.temporaryPatientCode} />
                                        <span className="flex flex-wrap items-center gap-2">
                                            <TriagePriorityBadge priority={e.latestTriagePriority} />
                                            <EncounterStatusBadge status={e.status} />
                                        </span>
                                    </Link>
                                </li>
                            ))}
                        </ul>
                    )}
                </QueryView>
            </Card>
        </div>
    );
}

type VitalValues = Partial<Record<VitalSignType, string>>;

function TriageForm({ encounter, onSaved }: { encounter: ClinicalEncounterSummary; onSaved: () => void }) {
    const feedback = useClinicalFeedback('triage');
    const canWriteVitals = useAuth().can(Permissions.VitalSigns.Write);
    // Sin valores por defecto: solo se envía lo que el usuario introduce.
    const [priority, setPriority] = useState<TriagePriority | null>(null);
    const [vitals, setVitals] = useState<VitalValues>({});
    const measuredAt = useMeasurementTime();
    const [reason, setReason] = useState('');
    const [observations, setObservations] = useState('');
    const [priorityError, setPriorityError] = useState<string>();
    const [saving, setSaving] = useState(false);

    const open = isEncounterOpen(encounter.status);

    const vitalError = (type: VitalSignType): string | undefined => {
        const raw = vitals[type];
        if (!raw) return undefined;
        const meta = vitalSignMeta(type);
        const value = Number(raw);
        if (Number.isNaN(value) || value < meta.min || value > meta.max) return `Valor fuera de rango (${meta.min}–${meta.max}).`;
        return undefined;
    };

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        if (saving) return;
        if (!priority) {
            setPriorityError('Selecciona la prioridad.');
            return;
        }
        if (VITAL_SIGNS.some((v) => vitalError(v.type))) return;

        // Sin unidad: la fija el backend (VitalSignLimits).
        const measuredAtUtc = measuredAt.toUtc();
        const measurements: VitalSignInput[] = VITAL_SIGNS.filter((v) => vitals[v.type]?.trim()).map((v) => ({
            type: v.type,
            value: Number(vitals[v.type]),
            measuredAtUtc,
        }));

        setSaving(true);
        try {
            await clinicalApi.createTriage(encounter.id, {
                priority,
                reason: reason.trim() || null,
                observations: observations.trim() || null,
                measurements,
            });
            feedback.success('triage registrado');
            onSaved();
        } catch (error) {
            feedback.failure('No se pudo registrar el triage.', error);
            setSaving(false);
        }
    };

    if (!open) {
        return (
            <Card title="Registrar triage">
                <p className="m-0 text-sm text-ink-muted">La atención está cerrada; no admite nuevos registros.</p>
            </Card>
        );
    }

    return (
        <Card title="Registrar triage">
            <form onSubmit={submit} noValidate className="flex flex-col gap-5">
                <fieldset className="m-0 border-0 p-0" aria-describedby={priorityError ? 'priority-error' : undefined}>
                    <legend className="mb-2 text-[13px] font-semibold text-ink-2">
                        Prioridad <span aria-hidden="true">*</span>
                    </legend>
                    <div className="grid grid-cols-1 gap-2 sm:grid-cols-5">
                        {TRIAGE_PRIORITIES.map((p) => (
                            <label
                                key={p.value}
                                className={cn(
                                    'flex cursor-pointer items-center gap-2 rounded-lg px-3 py-2 text-sm font-semibold ring-1 ring-inset sm:flex-col sm:text-center',
                                    'has-[:focus-visible]:outline has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-primary-600',
                                    p.className,
                                    priority === p.value ? 'ring-2 ring-ink' : 'opacity-80',
                                )}
                            >
                                <input
                                    type="radio"
                                    name="priority"
                                    value={p.value}
                                    checked={priority === p.value}
                                    onChange={() => {
                                        setPriority(p.value);
                                        setPriorityError(undefined);
                                    }}
                                    className="sr-only"
                                />
                                <span>{p.label}</span>
                                <span className="text-xs font-normal">{p.color}</span>
                            </label>
                        ))}
                    </div>
                    {priorityError && (
                        <p id="priority-error" role="alert" className="m-0 mt-1.5 text-xs text-danger-text">
                            {priorityError}
                        </p>
                    )}
                </fieldset>

                {canWriteVitals && (
                <fieldset className="m-0 flex flex-col gap-3 border-0 p-0">
                    <legend className="mb-1 text-[13px] font-semibold text-ink-2">Signos vitales (opcionales)</legend>
                    <p className="m-0 text-xs text-ink-muted">Deja vacío lo que no se haya medido. Cada valor se registra por separado.</p>
                    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                        {VITAL_SIGNS.map((v) => (
                            <Input
                                key={v.type}
                                label={`${v.label} (${v.unitLabel})`}
                                type="number"
                                inputMode="decimal"
                                min={v.min}
                                max={v.max}
                                step={v.step}
                                value={vitals[v.type] ?? ''}
                                onChange={(e) => setVitals((prev) => ({ ...prev, [v.type]: e.target.value }))}
                                error={vitalError(v.type)}
                            />
                        ))}
                    </div>
                    <Input
                        label="Hora de la medición"
                        type="datetime-local"
                        value={measuredAt.value}
                        onChange={(e) => measuredAt.change(e.target.value)}
                        required
                        containerClassName="sm:max-w-xs"
                    />
                </fieldset>
                )}

                <Textarea label="Motivo (opcional)" value={reason} onChange={(e) => setReason(e.target.value)} rows={2} />
                <Textarea label="Observaciones (opcional)" value={observations} onChange={(e) => setObservations(e.target.value)} rows={3} />

                <Button type="submit" loading={saving} loadingText="Guardando…" className="w-full sm:w-fit">
                    Guardar triage
                </Button>
            </form>
        </Card>
    );
}

/**
 * Hora de una medición en un <input type="datetime-local"> (precisión de minutos). Si el usuario no la cambia se envía
 * la hora exacta del envío: redondear al minuto podría dejarla antes del inicio de una atención recién abierta.
 */
function useMeasurementTime() {
    const [value, setValue] = useState(nowForDateTimeInput);
    const [edited, setEdited] = useState(false);
    return {
        value,
        change: (next: string) => {
            setValue(next);
            setEdited(true);
        },
        toUtc: () => (edited ? localInputToUtc(value) : new Date().toISOString()),
        reset: () => {
            setValue(nowForDateTimeInput());
            setEdited(false);
        },
    };
}

function VitalSignForm({ encounter, onSaved }: { encounter: ClinicalEncounterSummary; onSaved: () => void }) {
    const feedback = useClinicalFeedback('vitalSigns');
    const [type, setType] = useState<VitalSignType | ''>('');
    const [value, setValue] = useState('');
    const measuredAt = useMeasurementTime();
    const [saving, setSaving] = useState(false);
    const meta = type ? vitalSignMeta(type) : null;
    const numeric = Number(value);
    const error =
        meta && value && (Number.isNaN(numeric) || numeric < meta.min || numeric > meta.max)
            ? `Valor fuera de rango (${meta.min}–${meta.max}).`
            : undefined;

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        if (saving || !type || !value.trim() || error) return;
        setSaving(true);
        try {
            await clinicalApi.createVitalSign(encounter.id, { type, value: numeric, measuredAtUtc: measuredAt.toUtc() });
            feedback.success('signo vital registrado');
            setType('');
            setValue('');
            measuredAt.reset();
            onSaved();
        } catch (failure) {
            feedback.failure('No se pudo registrar el signo vital.', failure);
        } finally {
            setSaving(false);
        }
    };

    return (
        <form onSubmit={submit} noValidate className="mt-4 flex flex-col gap-3 border-t border-line-soft pt-4">
            <h3 className="m-0 text-sm font-semibold text-ink">Registrar signo vital</h3>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <Select
                    label="Signo"
                    value={type}
                    onChange={(e) => setType(e.target.value as VitalSignType)}
                    options={[
                        { value: '', label: 'Selecciona…' },
                        ...VITAL_SIGNS.map((v) => ({ value: v.type, label: `${v.label} (${v.unitLabel})` })),
                    ]}
                    required
                />
                <Input
                    label="Valor"
                    type="number"
                    inputMode="decimal"
                    min={meta?.min}
                    max={meta?.max}
                    step={meta?.step}
                    value={value}
                    onChange={(e) => setValue(e.target.value)}
                    error={error}
                    required
                />
            </div>
            <Input
                label="Hora de la medición"
                type="datetime-local"
                value={measuredAt.value}
                onChange={(e) => measuredAt.change(e.target.value)}
                required
                containerClassName="sm:max-w-xs"
            />
            <Button type="submit" variant="secondary" loading={saving} disabled={!type || !value.trim()} className="w-full sm:w-fit">
                Guardar signo vital
            </Button>
        </form>
    );
}

function TriageHistory({ encounter, onSaved }: { encounter: ClinicalEncounterSummary; onSaved: () => void }) {
    const encounterId = encounter.id;
    const canWriteVitals = useAuth().can(Permissions.VitalSigns.Write);
    const triages = useClinicalQuery(`triages:${encounterId}`, (signal) => clinicalApi.listTriages(encounterId, signal));
    const vitals = useClinicalQuery(`vitals:${encounterId}`, (signal) => clinicalApi.listVitalSigns(encounterId, signal));

    return (
        <div className="flex flex-col gap-5">
            <Card title="Historial de triage">
                <QueryView
                    state={triages.state}
                    onRetry={triages.reload}
                    errorTitle="No se pudo cargar el triage."
                    isEmpty={(list) => list.length === 0}
                    empty={<EmptyState title="Sin triage registrado" />}
                >
                    {(list) => (
                        <ul className="m-0 flex list-none flex-col gap-2 p-0">
                            {list.map((t) => (
                                <li key={t.id} className="flex flex-col gap-1 rounded-lg border border-line-soft p-3">
                                    <span className="flex flex-wrap items-center justify-between gap-2">
                                        <TriagePriorityBadge priority={t.priority} />
                                        <time dateTime={t.performedAtUtc} className="text-xs text-ink-muted">
                                            {formatDateTimeUtc(t.performedAtUtc)}
                                        </time>
                                    </span>
                                    {t.observations && <p className="m-0 text-[13px] text-ink-3">{t.observations}</p>}
                                </li>
                            ))}
                        </ul>
                    )}
                </QueryView>
            </Card>

            <Card title="Signos vitales">
                <QueryView
                    state={vitals.state}
                    onRetry={vitals.reload}
                    errorTitle="No se pudieron cargar los signos vitales."
                    isEmpty={(list) => list.length === 0}
                    empty={<EmptyState title="Sin mediciones" />}
                >
                    {(list) => (
                        <div className="overflow-x-auto">
                            <table className="w-full text-left text-sm">
                                <caption className="sr-only">Signos vitales registrados</caption>
                                <thead className="text-xs text-ink-muted">
                                    <tr>
                                        <th scope="col" className="py-1.5 pr-3 font-semibold">Signo</th>
                                        <th scope="col" className="py-1.5 pr-3 font-semibold">Valor</th>
                                        <th scope="col" className="py-1.5 font-semibold">Hora</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-line-soft">
                                    {list.map((v) => {
                                        const meta = vitalSignMeta(v.type);
                                        return (
                                            <tr key={v.id}>
                                                <td className="py-1.5 pr-3">{meta.label}</td>
                                                <td className="tabular py-1.5 pr-3">
                                                    {v.value} {meta.unitLabel}
                                                </td>
                                                <td className="py-1.5 text-ink-muted">{formatDateTimeUtc(v.measuredAtUtc)}</td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </div>
                    )}
                </QueryView>
                {canWriteVitals && isEncounterOpen(encounter.status) && (
                    <VitalSignForm
                        encounter={encounter}
                        onSaved={() => {
                            vitals.reload();
                            // Created -> InProgress: refresca la cabecera de la atención.
                            if (encounter.status === 'Created') onSaved();
                        }}
                    />
                )}
            </Card>
        </div>
    );
}
