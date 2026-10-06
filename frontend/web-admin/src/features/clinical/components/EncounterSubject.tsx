import { Badge } from '../../../components/shared';
import type { PatientSummary } from '../types/clinical.types';
import { IDENTIFIER_TYPE_LABELS, SEX_LABELS, ageFromBirthDate, shortTemporaryCode } from '../utils/clinicalLabels';

export interface EncounterSubjectProps {
    patient: PatientSummary | null;
    temporaryPatientCode: string | null;
    /** compact: para listas; full: código NN completo e identificador principal. */
    variant?: 'compact' | 'full';
}

/** Sujeto de una atención: paciente identificado o NN. Nunca asume que existe un Patient. */
export function EncounterSubject({ patient, temporaryPatientCode, variant = 'compact' }: EncounterSubjectProps) {
    if (!patient) {
        return (
            <span className="flex min-w-0 flex-col gap-0.5">
                <span className="flex flex-wrap items-center gap-2 font-semibold text-ink">
                    Paciente NN
                    <Badge tone="warning">No identificado</Badge>
                </span>
                {temporaryPatientCode && (
                    <span className="break-all font-mono text-xs text-ink-muted">
                        {variant === 'full' ? temporaryPatientCode : shortTemporaryCode(temporaryPatientCode)}
                    </span>
                )}
            </span>
        );
    }

    const age = ageFromBirthDate(patient.birthDate);
    const details = [age !== null ? `${age} años` : null, SEX_LABELS[patient.biologicalSex]].filter(Boolean).join(' · ');

    return (
        <span className="flex min-w-0 flex-col gap-0.5">
            <span className="flex flex-wrap items-center gap-2 font-semibold text-ink">
                <span className="min-w-0 truncate">{patient.displayName}</span>
                {patient.identityStatus === 'Unverified' && <Badge tone="warning">Identidad sin verificar</Badge>}
            </span>
            <span className="text-xs text-ink-muted">
                {details}
                {variant === 'full' && patient.primaryIdentifier && (
                    <>
                        {' · '}
                        {IDENTIFIER_TYPE_LABELS[patient.primaryIdentifier.type]}: {patient.primaryIdentifier.value}
                    </>
                )}
            </span>
        </span>
    );
}
