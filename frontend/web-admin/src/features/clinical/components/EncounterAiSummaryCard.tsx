import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { Info } from 'lucide-react';
import { Button, Card, FormError } from '../../../components/shared';
import { clinicalApi, describeError } from '../api';
import type {
    ClinicalAiSourceType,
    EncounterAiReviewPointKind,
    EncounterAiSection,
    EncounterAiSourceReference,
    EncounterAiSummary,
    Guid,
} from '../types/clinical.types';
import { formatDateTimeUtc } from '../utils/clinicalLabels';
import { SimulatedBadge } from './SimulatedBadge';

const SECTIONS: { key: keyof EncounterAiSummary['sections']; title: string }[] = [
    { key: 'course', title: 'Evolución' },
    { key: 'vitalSigns', title: 'Signos vitales' },
    { key: 'medications', title: 'Medicación' },
    { key: 'procedures', title: 'Procedimientos' },
    { key: 'notes', title: 'Notas clínicas' },
    { key: 'transfer', title: 'Traslado' },
];

const REVIEW_KINDS: Record<EncounterAiReviewPointKind, string> = {
    MissingDocumentation: 'Documentación faltante',
    Inconsistency: 'Inconsistencia en la documentación',
    PendingItem: 'Elemento documentado pendiente',
};

// Dónde se ve cada tipo de registro dentro de la atención (pestaña). Sin pestaña: se muestra solo el tipo.
const SOURCE_TYPES: Record<ClinicalAiSourceType, { label: string; tab?: string }> = {
    Triage: { label: 'Triage', tab: 'triage' },
    VitalSign: { label: 'Signo vital', tab: 'triage' },
    ClinicalNote: { label: 'Nota clínica', tab: '' },
    Diagnosis: { label: 'Diagnóstico', tab: '' },
    Prescription: { label: 'Prescripción', tab: 'medicacion' },
    MedicationAdministration: { label: 'Administración de medicamento', tab: 'medicacion' },
    ClinicalProcedure: { label: 'Procedimiento' },
    ClinicalTransfer: { label: 'Traslado' },
};

/**
 * Resumen asistido por IA de la atención (ADR-013). Solo lectura: no guarda nada, no crea notas ni diagnósticos y no
 * tiene acciones de "aplicar". Todo se muestra como texto plano (React lo escapa; nunca HTML del proveedor). El resultado
 * vive solo en el estado de este componente: es una instantánea y no se actualiza sola; se genera de nuevo a mano.
 */
export function EncounterAiSummaryCard({ encounterId }: { encounterId: Guid }) {
    const [summary, setSummary] = useState<EncounterAiSummary | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    // Evita una segunda generación antes de que React aplique `busy` (doble clic).
    const inFlight = useRef(false);
    const pending = useRef<AbortController | null>(null);

    // Salir de la atención cancela la generación en curso (el servidor deja de esperarla).
    useEffect(() => () => pending.current?.abort(), []);

    const generate = async () => {
        if (inFlight.current) return;
        inFlight.current = true;
        const controller = new AbortController();
        pending.current = controller;
        setBusy(true);
        setError(null);
        try {
            setSummary(await clinicalApi.generateEncounterAiSummary(encounterId, controller.signal));
        } catch (failure) {
            if (!controller.signal.aborted) setError(describeError(failure));
        } finally {
            inFlight.current = false;
            pending.current = null;
            if (!controller.signal.aborted) setBusy(false);
        }
    };

    return (
        <Card title="Resumen asistido por IA" actions={<SimulatedBadge feature="aiSummary" />}>
            <div className="flex flex-col gap-4">
                <p className="m-0 text-sm text-ink-muted">
                    Resume los registros documentados de esta atención y señala puntos de la documentación a revisar. No diagnostica,
                    no recomienda tratamientos y no modifica el expediente.
                </p>
                <Button
                    onClick={() => void generate()}
                    loading={busy}
                    loadingText="Generando resumen…"
                    disabled={busy}
                    className="w-full sm:w-fit"
                >
                    {summary ? 'Generar de nuevo' : 'Generar resumen asistido'}
                </Button>
                <FormError message={error} />
                {summary && <SummaryView encounterId={encounterId} summary={summary} />}
            </div>
        </Card>
    );
}

function SummaryView({ encounterId, summary }: { encounterId: Guid; summary: EncounterAiSummary }) {
    const sections = SECTIONS.filter(({ key }) => summary.sections[key] !== null);
    return (
        <section aria-label="Resumen generado por IA" className="flex flex-col gap-4 border-t border-line-soft pt-4">
            {/* Fijo y sin opción de cerrar: acompaña siempre al contenido generado. */}
            <div
                role="note"
                className="flex items-start gap-2.5 rounded-lg border border-warning-500/40 bg-warning-50 px-3.5 py-3 text-[13px] font-semibold text-warning-700"
            >
                <Info size={16} className="mt-0.5 shrink-0" aria-hidden="true" />
                <span>Generado por IA · Requiere revisión profesional</span>
            </div>

            <p className="m-0 whitespace-pre-wrap text-sm text-ink-2">{summary.summary}</p>

            {sections.map(({ key, title }) => (
                <SectionView key={key} encounterId={encounterId} title={title} section={summary.sections[key] as EncounterAiSection} />
            ))}

            <div className="flex flex-col gap-2">
                <h3 className="m-0 text-sm font-semibold text-ink">Puntos a revisar</h3>
                {summary.reviewPoints.length === 0 ? (
                    <p className="m-0 text-sm text-ink-muted">Sin puntos de documentación señalados.</p>
                ) : (
                    <ul aria-label="Puntos a revisar" className="m-0 flex list-none flex-col gap-2 p-0">
                        {summary.reviewPoints.map((point, index) => (
                            <li key={index} className="flex flex-col gap-1 rounded-lg border border-line-soft p-3">
                                <span className="text-xs font-semibold text-ink-2">{REVIEW_KINDS[point.kind]}</span>
                                <span className="whitespace-pre-wrap text-sm text-ink-3">{point.text}</span>
                                <Sources encounterId={encounterId} sources={point.sources} />
                            </li>
                        ))}
                    </ul>
                )}
            </div>

            {summary.notDocumented.length > 0 && (
                <div className="flex flex-col gap-1">
                    <h3 className="m-0 text-sm font-semibold text-ink">Datos no documentados</h3>
                    <ul aria-label="Datos no documentados" className="m-0 list-disc pl-5 text-sm text-ink-3">
                        {summary.notDocumented.map((entry, index) => (
                            <li key={index}>{entry}</li>
                        ))}
                    </ul>
                </div>
            )}

            <p className="m-0 text-xs text-ink-muted">
                Generado el {formatDateTimeUtc(summary.generatedAtUtc)}. Instantánea de los datos disponibles al momento de
                generación. No se actualiza automáticamente.
                {summary.truncated && ' La atención tiene más registros de los que admite el resumen: solo se incluyeron los más recientes.'}
            </p>
        </section>
    );
}

function SectionView({ encounterId, title, section }: { encounterId: Guid; title: string; section: EncounterAiSection }) {
    return (
        <div className="flex flex-col gap-1">
            <h3 className="m-0 text-sm font-semibold text-ink">{title}</h3>
            <p className="m-0 whitespace-pre-wrap text-sm text-ink-3">{section.text}</p>
            <Sources encounterId={encounterId} sources={section.sources} />
        </div>
    );
}

/**
 * Registros relacionados, agrupados por tipo, con enlace a la pestaña donde se ven. Solo sirven para navegar: el
 * servidor comprueba que existen en esta atención, no que respalden la frase.
 */
function Sources({ encounterId, sources }: { encounterId: Guid; sources: EncounterAiSourceReference[] }) {
    if (sources.length === 0) return null;
    const counts = new Map<ClinicalAiSourceType, number>();
    for (const source of sources) counts.set(source.type, (counts.get(source.type) ?? 0) + 1);
    const base = `/hospital/atenciones/${encounterId}`;

    return (
        <p className="m-0 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-ink-muted">
            <span>Registros relacionados:</span>
            {[...counts].map(([type, count]) => {
                const meta = SOURCE_TYPES[type];
                const text = `${meta.label} (${count})`;
                return meta.tab === undefined ? (
                    <span key={type}>{text}</span>
                ) : (
                    <Link key={type} to={meta.tab ? `${base}/${meta.tab}` : base} className="text-primary-600 no-underline hover:underline">
                        {text}
                    </Link>
                );
            })}
        </p>
    );
}
