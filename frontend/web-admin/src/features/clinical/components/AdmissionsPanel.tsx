import { useState } from 'react';
import { Badge, Button, Card, Checkbox, EmptyState } from '../../../components/shared';
import { clinicalApi } from '../api';
import { useClinicSelection } from '../hooks/useClinicSelection';
import { useClinicalQuery } from '../hooks/useClinicalQuery';
import type { AdmissionEncounterSummary } from '../types/clinical.types';
import { ENCOUNTER_TYPE_LABELS, formatDateTimeUtc, shortTemporaryCode } from '../utils/clinicalLabels';
import { EncounterStatusBadge } from './ClinicalBadges';
import { QueryView } from './QueryView';

export interface AdmissionsPanelProps {
    /** Cambia para forzar la recarga (p. ej. tras crear o identificar una atención). */
    version: number;
    identifyingId: string | null;
    onIdentify: (encounter: AdmissionEncounterSummary) => void;
}

/**
 * Atenciones abiertas vistas por Recepción (Patients.Identify). Proyección administrativa: sin motivo, triage ni datos
 * clínicos. Permite localizar una atención NN y empezar su identificación.
 */
export function AdmissionsPanel({ version, identifyingId, onIdentify }: AdmissionsPanelProps) {
    const clinic = useClinicSelection();
    const [unidentifiedOnly, setUnidentifiedOnly] = useState(false);
    const { state, reload } = useClinicalQuery(
        clinic.ready ? `admissions:${clinic.queryKey}:${unidentifiedOnly}:${version}` : null,
        (signal) => clinicalApi.listAdmissionEncounters({ unidentifiedOnly }, signal),
    );

    return (
        <Card
            title="Atenciones en admisión"
            subtitle="Atenciones abiertas de la clínica. Las NN pueden identificarse cuando se conoce al paciente."
            actions={<Checkbox label="Solo NN" checked={unidentifiedOnly} onChange={(e) => setUnidentifiedOnly(e.target.checked)} />}
        >
            <QueryView
                state={state}
                onRetry={reload}
                errorTitle="No se pudieron cargar las atenciones."
                isEmpty={(list) => list.length === 0}
                empty={<EmptyState title={unidentifiedOnly ? 'No hay atenciones NN abiertas' : 'No hay atenciones abiertas'} />}
            >
                {(list) => (
                    <ul className="m-0 flex list-none flex-col gap-2 p-0">
                        {list.map((a) => (
                            <li
                                key={a.encounterId}
                                className="flex flex-col gap-2 rounded-lg border border-line p-3 sm:flex-row sm:items-center sm:justify-between"
                            >
                                <span className="flex min-w-0 flex-col gap-0.5">
                                    <span className="flex flex-wrap items-center gap-2 font-semibold text-ink">
                                        {a.patientDisplayName ?? 'Paciente NN'}
                                        {!a.patientId && <Badge tone="warning">No identificado</Badge>}
                                    </span>
                                    <span className="text-xs text-ink-muted">
                                        {ENCOUNTER_TYPE_LABELS[a.type]} · {formatDateTimeUtc(a.startedAtUtc)}
                                        {a.temporaryPatientCode && (
                                            <span className="font-mono" title={a.temporaryPatientCode}>
                                                {' · '}
                                                {shortTemporaryCode(a.temporaryPatientCode)}
                                            </span>
                                        )}
                                    </span>
                                </span>
                                <span className="flex shrink-0 items-center gap-2">
                                    <EncounterStatusBadge status={a.status} />
                                    {!a.patientId && (
                                        <Button
                                            size="sm"
                                            variant={identifyingId === a.encounterId ? 'primary' : 'secondary'}
                                            onClick={() => onIdentify(a)}
                                        >
                                            {identifyingId === a.encounterId ? 'Identificando…' : 'Identificar'}
                                        </Button>
                                    )}
                                </span>
                            </li>
                        ))}
                    </ul>
                )}
            </QueryView>
        </Card>
    );
}
