import { useState, type FormEvent } from 'react';
import { Permissions, useAuth } from '../../../auth';
import { Link, useParams } from 'react-router-dom';
import { Button, Card, Checkbox, EmptyState, Input, Select, Textarea } from '../../../components/shared';
import { clinicalApi } from '../api';
import { EncounterStatusBadge, TriagePriorityBadge } from '../components/ClinicalBadges';
import { EncounterShell } from '../components/EncounterShell';
import { EncounterSubject } from '../components/EncounterSubject';
import { QueryView } from '../components/QueryView';
import { useClinicalFeedback } from '../hooks/useClinicalFeedback';
import { useClinicalQuery } from '../hooks/useClinicalQuery';
import type { ClinicalEncounterSummary, ClinicalNoteType, DiagnosisType } from '../types/clinical.types';
import {
    DIAGNOSIS_TYPE_LABELS,
    ENCOUNTER_TYPE_LABELS,
    NOTE_TYPE_LABELS,
    formatDateTimeUtc,
    isEncounterOpen,
    localInputToUtc,
    nowForDateTimeInput,
} from '../utils/clinicalLabels';

/** /hospital/atenciones: atenciones abiertas. /hospital/atenciones/:id: expediente de la atención. */
export function EncounterDashboardPage() {
    const { encounterId } = useParams();
    if (!encounterId) return <EncounterList />;

    return (
        <EncounterShell encounterId={encounterId}>
            {(encounter) => (
                <div className="grid gap-5 xl:grid-cols-2">
                    <DiagnosesCard encounter={encounter} />
                    <NotesCard encounter={encounter} />
                </div>
            )}
        </EncounterShell>
    );
}

function EncounterList() {
    const { state, reload } = useClinicalQuery('active-encounters', (signal) => clinicalApi.listActiveEncounters(signal));

    return (
        <div className="flex flex-col gap-5">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="m-0 text-2xl font-bold text-ink">Atenciones</h1>
                    <p className="m-0 mt-1 text-sm text-ink-muted">Atenciones abiertas en este hospital.</p>
                </div>
                <Link
                    to="/hospital/recepcion"
                    className="rounded-lg bg-primary-600 px-4 py-2 text-sm font-medium text-white no-underline hover:bg-primary-700 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-600"
                >
                    Nueva atención
                </Link>
            </div>
            <Card padding="none">
                <QueryView
                    state={state}
                    onRetry={reload}
                    errorTitle="No se pudieron cargar las atenciones."
                    isEmpty={(list) => list.length === 0}
                    empty={
                        <div className="p-4">
                            <EmptyState title="No hay atenciones abiertas" description="Las atenciones se abren desde Recepción." />
                        </div>
                    }
                >
                    {(list) => (
                        <ul className="m-0 list-none divide-y divide-line-soft p-0">
                            {list.map((e) => (
                                <li key={e.id}>
                                    <Link
                                        to={`/hospital/atenciones/${e.id}`}
                                        className="flex flex-col gap-2 px-4 py-3 text-ink no-underline hover:bg-surface-hover focus-visible:outline focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary-600 md:flex-row md:items-center md:justify-between"
                                    >
                                        <EncounterSubject patient={e.patient} temporaryPatientCode={e.temporaryPatientCode} />
                                        <span className="flex flex-wrap items-center gap-2 text-xs text-ink-muted">
                                            {ENCOUNTER_TYPE_LABELS[e.type]} · {formatDateTimeUtc(e.startedAtUtc)}
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

const CODE_SYSTEMS = [
    { value: 'ICD-10', label: 'CIE-10' },
    { value: 'SNOMED-CT', label: 'SNOMED CT' },
];

function DiagnosesCard({ encounter }: { encounter: ClinicalEncounterSummary }) {
    const canWrite = useAuth().can(Permissions.Diagnoses.Write);
    const feedback = useClinicalFeedback();
    const { state, reload } = useClinicalQuery(`diagnoses:${encounter.id}`, (signal) => clinicalApi.listDiagnoses(encounter.id, signal));
    const [code, setCode] = useState('');
    const [codeSystem, setCodeSystem] = useState('');
    const [description, setDescription] = useState('');
    const [type, setType] = useState<DiagnosisType | ''>('');
    const [isPrimary, setIsPrimary] = useState(false);
    const [saving, setSaving] = useState(false);
    const open = isEncounterOpen(encounter.status);

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        if (saving || !type || !description.trim()) return;
        setSaving(true);
        try {
            await clinicalApi.createDiagnosis(encounter.id, {
                code: code.trim() || null,
                codeSystem: code.trim() ? codeSystem || null : null,
                description: description.trim(),
                type,
                isPrimary,
            });
            feedback.success('diagnóstico registrado');
            setCode('');
            setDescription('');
            setType('');
            setIsPrimary(false);
            reload();
        } catch (error) {
            feedback.failure('No se pudo registrar el diagnóstico.', error);
        } finally {
            setSaving(false);
        }
    };

    return (
        <Card title="Diagnósticos">
            <QueryView
                state={state}
                onRetry={reload}
                errorTitle="No se pudieron cargar los diagnósticos."
                isEmpty={(list) => list.length === 0}
                empty={<EmptyState title="Sin diagnósticos registrados" />}
            >
                {(list) => (
                    <ul className="m-0 flex list-none flex-col gap-2 p-0">
                        {list.map((d) => (
                            <li key={d.id} className="flex flex-col gap-0.5 rounded-lg border border-line-soft p-3">
                                <span className="text-sm font-semibold text-ink">
                                    {d.code && <span className="font-mono">{d.code} · </span>}
                                    {d.description}
                                </span>
                                <span className="text-xs text-ink-muted">
                                    {DIAGNOSIS_TYPE_LABELS[d.type]}
                                    {d.isPrimary && ' · Principal'} · {formatDateTimeUtc(d.recordedAtUtc)}
                                </span>
                            </li>
                        ))}
                    </ul>
                )}
            </QueryView>

            {open && canWrite && (
                <form onSubmit={submit} className="mt-5 flex flex-col gap-3 border-t border-line-soft pt-4">
                    <h3 className="m-0 text-sm font-semibold text-ink">Registrar diagnóstico</h3>
                    <div className="grid gap-3 sm:grid-cols-2">
                        <Input label="Código (opcional)" value={code} onChange={(e) => setCode(e.target.value)} maxLength={32} />
                        <Select
                            label="Sistema de codificación"
                            value={codeSystem}
                            onChange={(e) => setCodeSystem(e.target.value)}
                            options={CODE_SYSTEMS}
                            placeholder="Sin codificar"
                            disabled={!code.trim()}
                        />
                    </div>
                    <Input label="Descripción" value={description} onChange={(e) => setDescription(e.target.value)} required maxLength={500} />
                    <div className="grid gap-3 sm:grid-cols-2 sm:items-end">
                        <Select
                            label="Tipo"
                            value={type}
                            onChange={(e) => setType(e.target.value as DiagnosisType | '')}
                            options={(Object.keys(DIAGNOSIS_TYPE_LABELS) as DiagnosisType[]).map((t) => ({ value: t, label: DIAGNOSIS_TYPE_LABELS[t] }))}
                            placeholder="Selecciona…"
                            required
                        />
                        <Checkbox label="Diagnóstico principal" checked={isPrimary} onChange={(e) => setIsPrimary(e.target.checked)} />
                    </div>
                    <Button type="submit" loading={saving} loadingText="Guardando…" disabled={!type || !description.trim()} className="w-full sm:w-fit">
                        Agregar diagnóstico
                    </Button>
                </form>
            )}
        </Card>
    );
}

// Ayuda de redacción: inserta encabezados SOAP en el contenido. El contrato sigue siendo type + content.
const SOAP_TEMPLATE = 'S: \nO: \nA: \nP: ';

function NotesCard({ encounter }: { encounter: ClinicalEncounterSummary }) {
    const canWrite = useAuth().can(Permissions.ClinicalNotes.Write);
    const feedback = useClinicalFeedback();
    const { state, reload } = useClinicalQuery(`notes:${encounter.id}`, (signal) => clinicalApi.listNotes(encounter.id, signal));
    const [type, setType] = useState<ClinicalNoteType | ''>('');
    const [content, setContent] = useState('');
    const [occurredAt, setOccurredAt] = useState(nowForDateTimeInput);
    const [saving, setSaving] = useState(false);
    const open = isEncounterOpen(encounter.status);

    // Discharge se registra al cerrar la atención; Prehospital y TransferSummary vienen del flujo paramédico.
    const noteTypes: ClinicalNoteType[] = ['Assessment', 'Evolution', 'Other'];

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        if (saving || !type || !content.trim()) return;
        setSaving(true);
        try {
            await clinicalApi.createNote(encounter.id, { type, content: content.trim(), occurredAtUtc: localInputToUtc(occurredAt) });
            feedback.success('nota clínica registrada');
            setType('');
            setContent('');
            setOccurredAt(nowForDateTimeInput());
            reload();
        } catch (error) {
            feedback.failure('No se pudo registrar la nota.', error);
        } finally {
            setSaving(false);
        }
    };

    return (
        <Card title="Notas clínicas">
            <QueryView
                state={state}
                onRetry={reload}
                errorTitle="No se pudieron cargar las notas."
                isEmpty={(list) => list.length === 0}
                empty={<EmptyState title="Sin notas registradas" />}
            >
                {(list) => (
                    <ul className="m-0 flex list-none flex-col gap-2 p-0">
                        {list.map((n) => (
                            <li key={n.id} className="flex flex-col gap-1 rounded-lg border border-line-soft p-3">
                                <span className="text-xs text-ink-muted">
                                    <span className="font-semibold text-ink-2">{NOTE_TYPE_LABELS[n.type]}</span> · {formatDateTimeUtc(n.occurredAtUtc)}
                                    {n.authorDisplayName && <> · {n.authorDisplayName}</>}
                                </span>
                                <p className="m-0 whitespace-pre-wrap text-sm text-ink-3">{n.content}</p>
                            </li>
                        ))}
                    </ul>
                )}
            </QueryView>

            {open && canWrite && (
                <form onSubmit={submit} className="mt-5 flex flex-col gap-3 border-t border-line-soft pt-4">
                    <h3 className="m-0 text-sm font-semibold text-ink">Registrar nota</h3>
                    <div className="grid gap-3 sm:grid-cols-2">
                        <Select
                            label="Tipo de nota"
                            value={type}
                            onChange={(e) => setType(e.target.value as ClinicalNoteType | '')}
                            options={noteTypes.map((t) => ({ value: t, label: NOTE_TYPE_LABELS[t] }))}
                            placeholder="Selecciona…"
                            required
                        />
                        <Input label="Fecha y hora" type="datetime-local" value={occurredAt} onChange={(e) => setOccurredAt(e.target.value)} required />
                    </div>
                    <Textarea label="Contenido" value={content} onChange={(e) => setContent(e.target.value)} rows={6} required />
                    <div className="flex flex-col gap-2 sm:flex-row">
                        <Button type="submit" loading={saving} loadingText="Guardando…" disabled={!type || !content.trim()}>
                            Guardar nota
                        </Button>
                        <Button variant="ghost" onClick={() => setContent((c) => (c.trim() ? c : SOAP_TEMPLATE))} disabled={content.trim() !== ''}>
                            Usar formato SOAP
                        </Button>
                    </div>
                </form>
            )}
        </Card>
    );
}
