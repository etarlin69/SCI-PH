import { useState, type FormEvent } from 'react';
import { Permissions, useAuth } from '../../../auth';
import { Badge, Button, Card, EmptyState, Input, Select, useToast } from '../../../components/shared';
import { describeError } from '../api';
import { resultsApi, type RecordResultValue } from '../api/resultsApi';
import { useClinicalQuery } from '../hooks/useClinicalQuery';
import type { ClinicalEncounterSummary } from '../types/clinical.types';
import {
    RESULT_STATUS_LABELS,
    RESULT_TYPE_LABELS,
    resultValue,
    type ClinicalResult,
    type ClinicalResultType,
} from '../types/results.types';
import { acceptsClinicalRecords, formatDateTimeUtc } from '../utils/clinicalLabels';
import { QueryView } from './QueryView';

const TONE: Record<ClinicalResult['status'], 'warning' | 'success' | 'info' | 'neutral'> = {
    Pending: 'warning',
    Final: 'success',
    Corrected: 'info',
    Cancelled: 'neutral',
};

/**
 * Resultados de la atención: el ciclo de vida del dominio (Pendiente → Final → Corregido, o Cancelado si está pendiente).
 * Leerlos es lectura clínica; pedir, registrar, corregir y cancelar requieren Results.Write y la atención abierta. Los
 * resultados Final y Corregido se publican al paciente en su app.
 */
export function EncounterResultsCard({ encounter }: { encounter: ClinicalEncounterSummary }) {
    const canWrite = useAuth().can(Permissions.Results.Write) && acceptsClinicalRecords(encounter.status);
    const toast = useToast();
    const { state, reload } = useClinicalQuery(`results:${encounter.id}`, (signal) => resultsApi.list(encounter.id, signal));
    const [type, setType] = useState<ClinicalResultType | ''>('');
    const [name, setName] = useState('');
    const [range, setRange] = useState('');
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        if (saving || !type || !name.trim()) return;
        setSaving(true);
        setError(null);
        try {
            await resultsApi.request(encounter.id, { type, name: name.trim(), referenceRange: range.trim() || null });
            toast({ title: 'Resultado solicitado', tone: 'success' });
            setType('');
            setName('');
            setRange('');
            reload();
        } catch (failure) {
            setError(describeError(failure));
        } finally {
            setSaving(false);
        }
    };

    return (
        <Card title="Resultados">
            <QueryView
                state={state}
                onRetry={reload}
                errorTitle="No se pudieron cargar los resultados"
                isEmpty={(list) => list.length === 0}
                empty={<EmptyState title="Sin resultados" description="Las órdenes de laboratorio e imagen aparecen aquí." />}
            >
                {(list) => (
                    <ul className="m-0 flex list-none flex-col gap-2 p-0">
                        {list.map((r) => (
                            <ResultItem key={r.id} encounterId={encounter.id} result={r} canWrite={canWrite} onChanged={reload} />
                        ))}
                    </ul>
                )}
            </QueryView>

            {canWrite && (
                <form onSubmit={submit} className="mt-5 flex flex-col gap-3 border-t border-line-soft pt-4">
                    <h3 className="m-0 text-sm font-semibold text-ink">Solicitar resultado</h3>
                    <div className="grid gap-3 sm:grid-cols-3">
                        <Select
                            label="Tipo"
                            value={type}
                            onChange={(e) => setType(e.target.value as ClinicalResultType | '')}
                            options={(Object.keys(RESULT_TYPE_LABELS) as ClinicalResultType[]).map((t) => ({ value: t, label: RESULT_TYPE_LABELS[t] }))}
                            placeholder="Selecciona…"
                            required
                        />
                        <Input label="Estudio" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} required />
                        <Input label="Rango de referencia" value={range} onChange={(e) => setRange(e.target.value)} maxLength={100} />
                    </div>
                    {error && (
                        <p role="alert" className="m-0 text-sm text-danger-text">
                            {error}
                        </p>
                    )}
                    <div>
                        <Button type="submit" loading={saving} loadingText="Guardando…" disabled={!type || !name.trim()}>
                            Solicitar
                        </Button>
                    </div>
                </form>
            )}
        </Card>
    );
}

function ResultItem({ encounterId, result, canWrite, onChanged }: { encounterId: string; result: ClinicalResult; canWrite: boolean; onChanged: () => void }) {
    const toast = useToast();
    const [editing, setEditing] = useState(false);
    const [text, setText] = useState('');
    const [numeric, setNumeric] = useState('');
    const [unit, setUnit] = useState(result.unit ?? '');
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const value = resultValue(result);
    const correcting = result.status === 'Final' || result.status === 'Corrected';
    const canRecord = canWrite && (result.status === 'Pending' || correcting);

    const save = async (e: FormEvent) => {
        e.preventDefault();
        const parsed = numeric.trim() === '' ? null : Number(numeric.replace(',', '.'));
        if (parsed !== null && Number.isNaN(parsed)) {
            setError('El valor numérico no es válido.');
            return;
        }
        const body: RecordResultValue = { resultText: text.trim() || null, numericValue: parsed, unit: unit.trim() || null };
        if (body.resultText === null && body.numericValue === null) {
            setError('Indica un valor numérico o un texto.');
            return;
        }
        setSaving(true);
        setError(null);
        try {
            await (correcting ? resultsApi.correct(encounterId, result.id, body) : resultsApi.recordFinal(encounterId, result.id, body));
            toast({ title: correcting ? 'Resultado corregido' : 'Resultado registrado', tone: 'success' });
            setEditing(false);
            onChanged();
        } catch (failure) {
            setError(describeError(failure));
        } finally {
            setSaving(false);
        }
    };

    const cancel = async () => {
        setSaving(true);
        try {
            await resultsApi.cancel(encounterId, result.id);
            toast({ title: 'Orden cancelada', tone: 'success' });
            onChanged();
        } catch (failure) {
            toast({ title: 'No se pudo cancelar la orden.', description: describeError(failure), tone: 'error' });
        } finally {
            setSaving(false);
        }
    };

    return (
        <li className="flex flex-col gap-2 rounded-lg border border-line-soft p-3">
            <span className="flex flex-wrap items-center gap-2">
                <span className="text-sm font-semibold text-ink">{result.name}</span>
                <Badge tone={TONE[result.status]}>{RESULT_STATUS_LABELS[result.status]}</Badge>
                <span className="text-xs text-ink-muted">{RESULT_TYPE_LABELS[result.type]}</span>
            </span>
            {value && <span className="tabular text-base font-semibold text-ink">{value}</span>}
            <span className="text-xs text-ink-muted">
                {result.referenceRange && <>Referencia: {result.referenceRange}</>}
                {result.resultedAtUtc && <> · {formatDateTimeUtc(result.resultedAtUtc)}</>}
                {result.recordedByDisplayName && <> · {result.recordedByDisplayName}</>}
            </span>

            {canRecord && !editing && (
                <div className="flex flex-wrap gap-2">
                    <Button size="sm" variant="secondary" onClick={() => setEditing(true)}>
                        {correcting ? 'Corregir' : 'Registrar resultado'}
                    </Button>
                    {result.status === 'Pending' && (
                        <Button size="sm" variant="ghost" onClick={cancel} loading={saving} loadingText="Cancelando…">
                            Cancelar orden
                        </Button>
                    )}
                </div>
            )}

            {editing && (
                <form onSubmit={save} className="flex flex-col gap-2 border-t border-line-soft pt-2" aria-label={`Valor de ${result.name}`}>
                    <div className="grid gap-2 sm:grid-cols-3">
                        <Input label="Valor numérico" inputMode="decimal" value={numeric} onChange={(e) => setNumeric(e.target.value)} />
                        <Input label="Unidad" value={unit} onChange={(e) => setUnit(e.target.value)} maxLength={50} />
                        <Input label="Resultado (texto)" value={text} onChange={(e) => setText(e.target.value)} maxLength={2000} />
                    </div>
                    {error && (
                        <p role="alert" className="m-0 text-sm text-danger-text">
                            {error}
                        </p>
                    )}
                    <div className="flex gap-2">
                        <Button size="sm" type="submit" loading={saving} loadingText="Guardando…">
                            Guardar
                        </Button>
                        <Button size="sm" variant="ghost" onClick={() => setEditing(false)}>
                            Volver
                        </Button>
                    </div>
                </form>
            )}
        </li>
    );
}
