import { useState, type FormEvent } from 'react';
import { Permissions, useAuth } from '../../../auth';
import { useNavigate } from 'react-router-dom';
import { Search, UserPlus, UserX, X } from 'lucide-react';
import { Button, Card, EmptyState, ErrorState, Input, LoadingSpinner, Select, Textarea } from '../../../components/shared';
import { clinicalApi, describeError } from '../api';
import { AdmissionsPanel } from '../components/AdmissionsPanel';
import { ClinicSelector } from '../components/ClinicSelector';
import { EncounterSubject } from '../components/EncounterSubject';
import { IncomingTransfersPanel } from '../components/IncomingTransfersPanel';
import { PatientRegistrationForm } from '../components/PatientRegistrationForm';
import { useClinicalFeedback } from '../hooks/useClinicalFeedback';
import type {
    AdmissionEncounterSummary,
    ClinicalEncounterSummary,
    ClinicalEncounterType,
    PatientIdentifierType,
    PatientSummary,
} from '../types/clinical.types';
import { ENCOUNTER_TYPE_LABELS, IDENTIFIER_TYPE_LABELS, shortTemporaryCode } from '../utils/clinicalLabels';

const SEARCH_BY: { value: PatientIdentifierType | 'Name'; label: string }[] = [
    { value: 'NationalId', label: IDENTIFIER_TYPE_LABELS.NationalId },
    { value: 'InternalCode', label: IDENTIFIER_TYPE_LABELS.InternalCode },
    { value: 'Passport', label: IDENTIFIER_TYPE_LABELS.Passport },
    { value: 'Name', label: 'Nombre' },
];

// Tipos que el hospital abre desde recepción. Prehospital lo crea la app paramédica (Rol 6); el BFF lo rechaza.
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
    const feedback = useClinicalFeedback('encounters');
    const identifyFeedback = useClinicalFeedback('admissions');
    const { can } = useAuth();
    const canSearch = can(Permissions.Patients.Read);
    const canCreate = can(Permissions.Encounters.Create);
    const canRegister = can(Permissions.Patients.Create);
    const canIdentify = can(Permissions.Patients.Identify);

    const [searchBy, setSearchBy] = useState<PatientIdentifierType | 'Name'>('NationalId');
    const [term, setTerm] = useState('');
    const [search, setSearch] = useState<SearchState>({ status: 'idle' });
    const [registering, setRegistering] = useState(false);

    const [encounterType, setEncounterType] = useState<ClinicalEncounterType>('Emergency');
    const [reason, setReason] = useState('');
    const [creating, setCreating] = useState<string | null>(null);
    const [created, setCreated] = useState<ClinicalEncounterSummary | null>(null);

    // Atención NN que se está identificando: los resultados de la búsqueda ofrecen «Asignar» en vez de «Abrir atención».
    const [identifying, setIdentifying] = useState<AdmissionEncounterSummary | null>(null);
    const [assigning, setAssigning] = useState<string | null>(null);
    const [admissionsVersion, setAdmissionsVersion] = useState(0);
    const refreshAdmissions = () => setAdmissionsVersion((v) => v + 1);

    const runSearch = async (e: FormEvent) => {
        e.preventDefault();
        if (!term.trim()) return;
        setSearch({ status: 'loading' });
        try {
            // POST con cuerpo: la cédula/pasaporte nunca va en la URL.
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
            // Solo se navega a donde los permisos reales lo permiten (Recepción no tiene Encounters.Read ni Triage.Write).
            if (can(Permissions.Triage.Write)) navigate(`/hospital/atenciones/${encounter.id}/triage`);
            else if (can(Permissions.Encounters.Read)) navigate(`/hospital/atenciones/${encounter.id}`);
            else {
                // El POST devuelve el resumen creado: Recepción continúa sin lectura clínica.
                setCreated(encounter);
                setCreating(null);
                setReason('');
                refreshAdmissions();
            }
        } catch (error) {
            feedback.failure('No se pudo crear la atención.', error);
            setCreating(null);
        }
    };

    const assignPatient = async (patient: PatientSummary) => {
        if (!identifying) return;
        setAssigning(patient.id);
        try {
            await clinicalApi.identifyEncounterPatient(identifying.encounterId, patient.id);
            identifyFeedback.success('paciente identificado');
            setIdentifying(null);
            refreshAdmissions();
        } catch (error) {
            identifyFeedback.failure('No se pudo identificar la atención.', error);
        } finally {
            setAssigning(null);
        }
    };

    const nnAllowed = NN_ALLOWED.includes(encounterType);

    return (
        <div className="flex flex-col gap-5">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="m-0 text-2xl font-bold text-ink">Recepción</h1>
                    <p className="m-0 mt-1 text-sm text-ink-muted">
                        Identifica al paciente o abre una atención NN, y recibe los traslados entrantes.
                    </p>
                </div>
                <ClinicSelector />
            </div>

            {created && (
                <p role="status" className="m-0 flex items-start justify-between gap-3 rounded-lg bg-success-50 px-3 py-2 text-sm text-success-700">
                    <span>
                        Atención creada: {ENCOUNTER_TYPE_LABELS[created.type]} ·{' '}
                        {created.patient?.displayName ?? (
                            <span className="font-mono" title={created.temporaryPatientCode ?? undefined}>
                                {created.temporaryPatientCode}
                            </span>
                        )}
                    </span>
                    <button type="button" aria-label="Cerrar aviso" className="text-success-700" onClick={() => setCreated(null)}>
                        <X size={16} aria-hidden="true" />
                    </button>
                </p>
            )}

            <div className="grid gap-5 xl:grid-cols-2">
                <Card title={identifying ? 'Identificar atención NN' : 'Nueva atención'}>
                    <div className="flex flex-col gap-4">
                        {identifying ? (
                            <div className="flex flex-col gap-2 rounded-lg bg-warning-50 px-3 py-2 text-[13px] text-warning-700 sm:flex-row sm:items-center sm:justify-between">
                                <span>
                                    Busca al paciente real y asígnalo a la atención{' '}
                                    <span className="font-mono">{shortTemporaryCode(identifying.temporaryPatientCode ?? '')}</span>. El código NN se
                                    conserva como historial.
                                </span>
                                <Button size="sm" variant="secondary" onClick={() => setIdentifying(null)}>
                                    Cancelar
                                </Button>
                            </div>
                        ) : (
                            <>
                                <div className="grid gap-4 sm:grid-cols-2">
                                    <Select
                                        label="Tipo de atención"
                                        value={encounterType}
                                        onChange={(e) => setEncounterType(e.target.value as ClinicalEncounterType)}
                                        options={HOSPITAL_ENCOUNTER_TYPES.map((t) => ({ value: t, label: ENCOUNTER_TYPE_LABELS[t] }))}
                                    />
                                </div>
                                <Textarea
                                    label="Motivo (opcional)"
                                    value={reason}
                                    onChange={(e) => setReason(e.target.value)}
                                    rows={2}
                                    maxLength={2000}
                                />
                            </>
                        )}

                        {canSearch && (
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
                                {search.status === 'error' && <ErrorState variant="banner" title="La búsqueda falló." message={search.error} />}
                                {search.status === 'success' &&
                                    (search.results.length === 0 ? (
                                        <EmptyState
                                            title="Sin coincidencias"
                                            description={
                                                identifying
                                                    ? 'Verifica el dato o registra al paciente si es nuevo.'
                                                    : 'Verifica el dato, registra al paciente o, si no es posible identificarlo, abre una atención NN.'
                                            }
                                        />
                                    ) : (
                                        <ul className="m-0 flex list-none flex-col gap-2 p-0">
                                            {search.results.map((p) => (
                                                <li
                                                    key={p.id}
                                                    className="flex flex-col gap-2 rounded-lg border border-line p-3 sm:flex-row sm:items-center sm:justify-between"
                                                >
                                                    <EncounterSubject patient={p} temporaryPatientCode={null} variant="full" />
                                                    {identifying ? (
                                                        <Button
                                                            size="sm"
                                                            className="shrink-0"
                                                            loading={assigning === p.id}
                                                            disabled={assigning !== null}
                                                            onClick={() => void assignPatient(p)}
                                                        >
                                                            Asignar a la atención NN
                                                        </Button>
                                                    ) : (
                                                        <Button
                                                            size="sm"
                                                            className="shrink-0"
                                                            loading={creating === p.id}
                                                            disabled={creating !== null || !canCreate}
                                                            onClick={() => void createEncounter(p)}
                                                        >
                                                            Abrir atención
                                                        </Button>
                                                    )}
                                                </li>
                                            ))}
                                        </ul>
                                    ))}

                                {canRegister &&
                                    (registering ? (
                                        <section aria-labelledby="register-title" className="flex flex-col gap-3 rounded-lg border border-line-soft p-3">
                                            <div className="flex items-center justify-between gap-2">
                                                <h3 id="register-title" className="m-0 text-sm font-semibold text-ink">
                                                    Registrar paciente nuevo
                                                </h3>
                                                <Button size="sm" variant="secondary" onClick={() => setRegistering(false)}>
                                                    Cancelar
                                                </Button>
                                            </div>
                                            <PatientRegistrationForm
                                                onRegistered={(patient) => {
                                                    setRegistering(false);
                                                    setSearch({ status: 'success', results: [patient] });
                                                }}
                                            />
                                        </section>
                                    ) : (
                                        <Button
                                            variant="secondary"
                                            className="w-fit"
                                            leftIcon={<UserPlus size={16} aria-hidden="true" />}
                                            onClick={() => setRegistering(true)}
                                        >
                                            Registrar paciente nuevo
                                        </Button>
                                    ))}
                            </section>
                        )}

                        {!identifying && (
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
                                    disabled={!nnAllowed || creating !== null || !canCreate}
                                    onClick={() => void createEncounter(null)}
                                >
                                    Abrir atención NN
                                </Button>
                                {!nnAllowed && (
                                    <p className="m-0 text-xs text-warning-700">Solo una atención de emergencia admite un paciente sin identificar.</p>
                                )}
                            </section>
                        )}
                    </div>
                </Card>

                <div className="flex flex-col gap-5">
                    {canIdentify && (
                        <AdmissionsPanel
                            version={admissionsVersion}
                            identifyingId={identifying?.encounterId ?? null}
                            onIdentify={(encounter) => {
                                setIdentifying(encounter);
                                setSearch({ status: 'idle' });
                            }}
                        />
                    )}
                    {can(Permissions.Transfers.Read) && <IncomingTransfersPanel />}
                </div>
            </div>
        </div>
    );
}
