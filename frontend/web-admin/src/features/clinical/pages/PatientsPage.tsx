import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { Search } from 'lucide-react';
import { Permissions, useAuth } from '../../../auth';
import { Button, Card, EmptyState, ErrorState, Input, LoadingSpinner, PageHeader, Select } from '../../../components/shared';
import { clinicalApi, describeError } from '../api';
import { EncounterSubject } from '../components/EncounterSubject';
import { PatientRegistrationForm } from '../components/PatientRegistrationForm';
import type { PatientIdentifierType, PatientSummary } from '../types/clinical.types';
import { IDENTIFIER_TYPE_LABELS } from '../utils/clinicalLabels';

const SEARCH_BY: { value: PatientIdentifierType | 'Name'; label: string }[] = [
    { value: 'NationalId', label: IDENTIFIER_TYPE_LABELS.NationalId },
    { value: 'InternalCode', label: IDENTIFIER_TYPE_LABELS.InternalCode },
    { value: 'Passport', label: IDENTIFIER_TYPE_LABELS.Passport },
    { value: 'Name', label: 'Nombre' },
];

type SearchState =
    | { status: 'idle' }
    | { status: 'loading' }
    | { status: 'error'; error: string }
    | { status: 'success'; results: PatientSummary[] };

/** Pacientes: búsqueda (Patients.Read; identificadores en el cuerpo, enmascarados en la respuesta), registro (Patients.Create) y acceso al detalle. */
export function PatientsPage() {
    const canRegister = useAuth().can(Permissions.Patients.Create);
    const [searchBy, setSearchBy] = useState<PatientIdentifierType | 'Name'>('NationalId');
    const [term, setTerm] = useState('');
    const [search, setSearch] = useState<SearchState>({ status: 'idle' });

    const runSearch = async (e: FormEvent) => {
        e.preventDefault();
        if (!term.trim()) return;
        setSearch({ status: 'loading' });
        try {
            const results = await clinicalApi.searchPatients({ identifierType: searchBy === 'Name' ? undefined : searchBy, value: term.trim() });
            setSearch({ status: 'success', results });
        } catch (error) {
            setSearch({ status: 'error', error: describeError(error) });
        }
    };

    return (
        <div className="flex flex-col gap-5">
            <PageHeader title="Pacientes" description="Búsqueda y registro de pacientes." />
            <div className="grid gap-5 xl:grid-cols-2">
                <Card title="Buscar paciente">
                    <div className="flex flex-col gap-3">
                        <form onSubmit={runSearch} className="flex flex-col gap-3 sm:flex-row sm:items-end">
                            <Select
                                label="Buscar por"
                                value={searchBy}
                                onChange={(e) => setSearchBy(e.target.value as PatientIdentifierType | 'Name')}
                                options={SEARCH_BY}
                                containerClassName="sm:w-44"
                            />
                            <Input label="Valor" value={term} onChange={(e) => setTerm(e.target.value)} autoComplete="off" required containerClassName="min-w-0 flex-1" />
                            <Button type="submit" leftIcon={<Search size={16} aria-hidden="true" />} loading={search.status === 'loading'}>
                                Buscar
                            </Button>
                        </form>
                        {search.status === 'loading' && <LoadingSpinner label="Buscando…" />}
                        {search.status === 'error' && <ErrorState variant="banner" title="La búsqueda falló." message={search.error} />}
                        {search.status === 'success' &&
                            (search.results.length === 0 ? (
                                <EmptyState title="Sin coincidencias" />
                            ) : (
                                <ul className="m-0 flex list-none flex-col gap-2 p-0">
                                    {search.results.map((p) => (
                                        <li key={p.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-line p-3">
                                            <EncounterSubject patient={p} temporaryPatientCode={null} variant="full" />
                                            <Link
                                                to={`/hospital/pacientes/${encodeURIComponent(p.id)}`}
                                                className="text-sm font-medium text-primary-600 hover:underline"
                                                aria-label={`Abrir ${p.displayName}`}
                                            >
                                                Abrir
                                            </Link>
                                        </li>
                                    ))}
                                </ul>
                            ))}
                    </div>
                </Card>
                {canRegister && (
                    <Card title="Registrar paciente" subtitle="El identificador se normaliza en el backend y se muestra enmascarado.">
                        <PatientRegistrationForm onRegistered={(patient) => setSearch({ status: 'success', results: [patient] })} />
                    </Card>
                )}
            </div>
        </div>
    );
}
