import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Search, UserX } from 'lucide-react';
import { Button, Card, ErrorState, Input, LoadingSpinner, Select } from '../../../components/shared';
import { clinicalApi, describeError } from '../api';
import { EncounterSubject } from '../components/EncounterSubject';
import { IncomingTransfersPanel } from '../components/IncomingTransfersPanel';
import { EmptyState } from '../components/QueryView';
import { TextAreaField } from '../components/TextAreaField';
import { useClinicalFeedback } from '../hooks/useClinicalFeedback';
import type { ClinicalEncounterType, PatientIdentifierType, PatientSummary } from '../types/clinical.types';
import { ENCOUNTER_TYPE_LABELS, IDENTIFIER_TYPE_LABELS } from '../utils/clinicalLabels';

const SEARCH_BY: { value: PatientIdentifierType | 'Name'; label: string }[] = [
    { value: 'NationalId', label: IDENTIFIER_TYPE_LABELS.NationalId },
    { value: 'InternalCode', label: IDENTIFIER_TYPE_LABELS.InternalCode },
    { value: 'Passport', label: IDENTIFIER_TYPE_LABELS.Passport },
    { value: 'Name', label: 'Nombre' },
];

// Tipos que el hospital abre desde recepción. Prehospital lo crea la app paramédica (Rol 6).
const HOSPITAL_ENCOUNTER_TYPES: ClinicalEncounterType[] = ['Emergency', 'Consultation', 'Hospitalization'];
// ClinicalEncounter.TypesAllowingUnidentifiedPatient (Domain), sin Prehospital.
const NN_ALLOWED: ClinicalEncounterType[] = ['Emergency'];

type SearchState =
    | { status: 'idle' }
    | { status: 'loading' }
    | { status: 'error'; error: string }
    | { status: 'success'; results: PatientSummary[] };

export function ReceptionPage() {
    const navigate = useNavigate();
    const feedback = useClinicalFeedback();

    const [searchBy, setSearchBy] = useState<PatientIdentifierType | 'Name'>('NationalId');
    const [term, setTerm] = useState('');
    const [search, setSearch] = useState<SearchState>({ status: 'idle' });

    const [encounterType, setEncounterType] = useState<ClinicalEncounterType>('Emergency');
    const [reason, setReason] = useState('');
    const [creating, setCreating] = useState<string | null>(null);

    const runSearch = async (e: FormEvent) => {
        e.preventDefault();
        if (!term.trim()) return;
        setSearch({ status: 'loading' });
        try {
            const results = await clinicalApi.searchPatients({
                identifierType: searchBy === 'Name' ? undefined : searchBy,
                value: term.trim(),
            });
            setSearch({ status: 'success', results });
        } catch (error) {
            setSearch({ status: 'error', error: describeError(error) });
        }
    };

    const createEncounter = async (patient: PatientSummary | null) => {
        setCreating(patient?.id ?? 'NN');
        try {
            const encounter = await clinicalApi.createEncounter({
                type: encounterType,
                patientId: patient?.id ?? null,
                reason: reason.trim() || null,
            });
            feedback.success(patient ? 'atención creada' : 'atención NN creada');
            navigate(`/hospital/atenciones/${encounter.id}/triage`);
        } catch (error) {
            feedback.failure('No se pudo crear la atención.', error);
            setCreating(null);
        }
    };

    const nnAllowed = NN_ALLOWED.includes(encounterType);

    return (
        <div className="flex flex-col gap-5">
            <div>
                <h1 className="m-0 text-2xl font-bold text-ink">Recepción</h1>
                <p className="m-0 mt-1 text-sm text-ink-muted">Identifica al paciente o abre una atención NN, y recibe los traslados entrantes.</p>
            </div>

            <div className="grid gap-5 xl:grid-cols-2">
                <Card title="Nueva atención">
                    <div className="flex flex-col gap-4">
                        <div className="grid gap-4 sm:grid-cols-2">
                            <Select
                                label="Tipo de atención"
                                value={encounterType}
                                onChange={(e) => setEncounterType(e.target.value as ClinicalEncounterType)}
                                options={HOSPITAL_ENCOUNTER_TYPES.map((t) => ({ value: t, label: ENCOUNTER_TYPE_LABELS[t] }))}
                            />
                        </div>
                        <TextAreaField label="Motivo (opcional)" value={reason} onChange={(e) => setReason(e.target.value)} rows={2} maxLength={2000} />

                        <section aria-labelledby="identified-title" className="flex flex-col gap-3 border-t border-line-soft pt-4">
                            <h3 id="identified-title" className="m-0 text-sm font-semibold text-ink">
                                Paciente identificado
                            </h3>
                            <form onSubmit={runSearch} className="flex flex-col gap-3 sm:flex-row sm:items-end">
                                <Select
                                    label="Buscar por"
                                    value={searchBy}
                                    onChange={(e) => setSearchBy(e.target.value as PatientIdentifierType | 'Name')}
                                    options={SEARCH_BY}
                                    containerClassName="sm:w-44"
                                />
                                <Input
                                    label="Valor"
                                    value={term}
                                    onChange={(e) => setTerm(e.target.value)}
                                    autoComplete="off"
                                    required
                                    containerClassName="min-w-0 flex-1"
                                />
                                <Button type="submit" leftIcon={<Search size={16} aria-hidden="true" />} loading={search.status === 'loading'}>
                                    Buscar
                                </Button>
                            </form>

                            {search.status === 'loading' && <LoadingSpinner label="Buscando…" />}
                            {search.status === 'error' && <ErrorState variant="inline" title="La búsqueda falló." description={search.error} />}
                            {search.status === 'success' &&
                                (search.results.length === 0 ? (
                                    <EmptyState
                                        title="Sin coincidencias"
                                        description="Verifica el dato o, si no es posible identificarlo, abre una atención NN."
                                    />
                                ) : (
                                    <ul className="m-0 flex list-none flex-col gap-2 p-0">
                                        {search.results.map((p) => (
                                            <li
                                                key={p.id}
                                                className="flex flex-col gap-2 rounded-lg border border-line p-3 sm:flex-row sm:items-center sm:justify-between"
                                            >
                                                <EncounterSubject patient={p} temporaryPatientCode={null} variant="full" />
                                                <Button
                                                    size="sm"
                                                    className="shrink-0"
                                                    loading={creating === p.id}
                                                    disabled={creating !== null}
                                                    onClick={() => void createEncounter(p)}
                                                >
                                                    Abrir atención
                                                </Button>
                                            </li>
                                        ))}
                                    </ul>
                                ))}
                        </section>

                        <section aria-labelledby="nn-title" className="flex flex-col gap-2 border-t border-line-soft pt-4">
                            <h3 id="nn-title" className="m-0 text-sm font-semibold text-ink">
                                Paciente no identificado (NN)
                            </h3>
                            <p className="m-0 text-[13px] text-ink-muted">
                                El backend asigna un código temporal NN. La identificación puede completarse después sin perder los registros.
                            </p>
                            <Button
                                variant="secondary"
                                className="w-fit"
                                leftIcon={<UserX size={16} aria-hidden="true" />}
                                loading={creating === 'NN'}
                                disabled={!nnAllowed || creating !== null}
                                onClick={() => void createEncounter(null)}
                            >
                                Abrir atención NN
                            </Button>
                            {!nnAllowed && (
                                <p className="m-0 text-xs text-warning-700">Solo una atención de emergencia admite un paciente sin identificar.</p>
                            )}
                        </section>
                    </div>
                </Card>

                <IncomingTransfersPanel />
            </div>
        </div>
    );
}
