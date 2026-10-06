import { Badge } from '../../../components/shared';
import { cn } from '../../../utils/cn';
import type { ClinicalEncounterStatus, ClinicalTransferStatus, TriagePriority } from '../types/clinical.types';
import { ENCOUNTER_STATUS, TRANSFER_STATUS, triagePriorityMeta } from '../utils/clinicalLabels';

export function TriagePriorityBadge({ priority }: { priority: TriagePriority | null }) {
    if (!priority) return <Badge tone="neutral">Sin triage</Badge>;
    const meta = triagePriorityMeta(priority);
    return (
        <span
            className={cn('inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold ring-1 ring-inset', meta.className)}
        >
            <span aria-hidden="true" className="h-2 w-2 rounded-full bg-current" />
            {meta.label}
            <span className="sr-only"> (prioridad {meta.color.toLowerCase()})</span>
        </span>
    );
}

export function EncounterStatusBadge({ status }: { status: ClinicalEncounterStatus }) {
    const meta = ENCOUNTER_STATUS[status];
    return <Badge tone={meta.tone}>{meta.label}</Badge>;
}

export function TransferStatusBadge({ status }: { status: ClinicalTransferStatus }) {
    const meta = TRANSFER_STATUS[status];
    return <Badge tone={meta.tone}>{meta.label}</Badge>;
}
