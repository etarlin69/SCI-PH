import type { ReactNode } from 'react';
import { Link, NavLink } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import { Card } from '../../../components/shared';
import { cn } from '../../../utils/cn';
import { clinicalApi } from '../api';
import { useClinicalQuery } from '../hooks/useClinicalQuery';
import type { ClinicalEncounterSummary, Guid } from '../types/clinical.types';
import { ENCOUNTER_TYPE_LABELS, formatDateTimeUtc } from '../utils/clinicalLabels';
import { EncounterStatusBadge, TriagePriorityBadge } from './ClinicalBadges';
import { EncounterSubject } from './EncounterSubject';
import { QueryView } from './QueryView';

const TABS = [
    { to: '', label: 'Expediente', end: true },
    { to: 'triage', label: 'Triage', end: false },
    { to: 'medicacion', label: 'Medicación y egreso', end: false },
];

export interface EncounterShellProps {
    encounterId: Guid;
    children: (encounter: ClinicalEncounterSummary, reloadEncounter: () => void) => ReactNode;
}

/** Cabecera común de una atención (sujeto, estado, prioridad) y pestañas entre sus vistas. */
export function EncounterShell({ encounterId, children }: EncounterShellProps) {
    const { state, reload } = useClinicalQuery(encounterId, (signal) => clinicalApi.getEncounter(encounterId, signal));
    const base = `/hospital/atenciones/${encounterId}`;

    return (
        <div className="flex flex-col gap-4">
            <Link
                to="/hospital/atenciones"
                className="inline-flex w-fit items-center gap-1.5 text-sm text-primary-600 no-underline hover:underline"
            >
                <ArrowLeft size={16} aria-hidden="true" />
                Atenciones abiertas
            </Link>
            <QueryView state={state} onRetry={reload} errorTitle="No se pudo cargar la atención." loadingLabel="Cargando atención…">
                {(encounter) => (
                    <>
                        <Card as="div">
                            <div className="flex flex-wrap items-start justify-between gap-3">
                                <EncounterSubject patient={encounter.patient} temporaryPatientCode={encounter.temporaryPatientCode} variant="full" />
                                <div className="flex flex-wrap items-center gap-2">
                                    <TriagePriorityBadge priority={encounter.latestTriagePriority} />
                                    <EncounterStatusBadge status={encounter.status} />
                                </div>
                            </div>
                            <p className="m-0 mt-3 text-[13px] text-ink-muted">
                                {ENCOUNTER_TYPE_LABELS[encounter.type]} · Inicio {formatDateTimeUtc(encounter.startedAtUtc)}
                                {encounter.reason && <> · {encounter.reason}</>}
                            </p>
                            <nav aria-label="Secciones de la atención" className="mt-4 flex flex-wrap gap-1 border-t border-line-soft pt-3">
                                {TABS.map((tab) => (
                                    <NavLink
                                        key={tab.label}
                                        to={tab.to ? `${base}/${tab.to}` : base}
                                        end={tab.end}
                                        className={({ isActive }) =>
                                            cn(
                                                'rounded-md px-3 py-1.5 text-sm no-underline',
                                                'focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-600',
                                                isActive ? 'bg-primary-100 font-semibold text-primary-700' : 'text-ink-3 hover:bg-surface-hover',
                                            )
                                        }
                                    >
                                        {tab.label}
                                    </NavLink>
                                ))}
                            </nav>
                        </Card>
                        {children(encounter, reload)}
                    </>
                )}
            </QueryView>
        </div>
    );
}
