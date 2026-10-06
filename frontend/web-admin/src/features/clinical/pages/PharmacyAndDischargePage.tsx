import { useState, type FormEvent } from 'react';
import { Permissions, useAuth } from '../../../auth';
import { useParams } from 'react-router-dom';
import { Badge, Button, Card, ConfirmDialog, EmptyState, Input, Select, Textarea } from '../../../components/shared';
import { clinicalApi } from '../api';
import { EncounterShell } from '../components/EncounterShell';
import { QueryView } from '../components/QueryView';
import { useClinicalFeedback } from '../hooks/useClinicalFeedback';
import { useClinicalQuery } from '../hooks/useClinicalQuery';
import type { ClinicalEncounterSummary, MedicationRoute, PrescriptionStatus } from '../types/clinical.types';
import { CLOSABLE_STATUSES, ENCOUNTER_STATUS, ROUTE_LABELS, formatDateTimeUtc, isEncounterOpen } from '../utils/clinicalLabels';

/** /hospital/atenciones/:id/medicacion: prescripción, administración (Kardex) y egreso de la atención. */
export function PharmacyAndDischargePage() {
    const { encounterId } = useParams();
    if (!encounterId) return null;

    return (
        <EncounterShell encounterId={encounterId}>
            {(encounter, reloadEncounter) => (
                <div className="grid gap-5 xl:grid-cols-2">
                    <PrescriptionsCard encounter={encounter} />
                    <div className="flex flex-col gap-5">
                        <AdministrationsCard encounterId={encounter.id} />
                        <DischargeCard encounter={encounter} onClosed={reloadEncounter} />
                    </div>
                </div>
            )}
        </EncounterShell>
    );
}

const PRESCRIPTION_STATUS: Record<PrescriptionStatus, string> = { Draft: 'Borrador', Issued: 'Emitida', Cancelled: 'Anulada' };

function PrescriptionsCard({ encounter }: { encounter: ClinicalEncounterSummary }) {
    const canPrescribe = useAuth().can(Permissions.Prescriptions.Write);
    const feedback = useClinicalFeedback();
    const prescriptions = useClinicalQuery(`prescriptions:${encounter.id}`, (signal) => clinicalApi.listPrescriptions(encounter.id, signal));
    const catalog = useClinicalQuery('medications', (signal) => clinicalApi.listMedications(signal));
    const open = isEncounterOpen(encounter.status);

    const [medicationId, setMedicationId] = useState('');
    const [dose, setDose] = useState('');
    const [doseUnit, setDoseUnit] = useState('');
    const [route, setRoute] = useState<MedicationRoute | ''>('');
    const [frequency, setFrequency] = useState('');
    const [durationDays, setDurationDays] = useState('');
    const [instructions, setInstructions] = useState('');
    const [saving, setSaving] = useState(false);

    const medications = catalog.state.status === 'success' ? catalog.state.data : [];
    const selected = medications.find((m) => m.id === medicationId);
    const doseValue = Number(dose);
    const valid = Boolean(medicationId && route && doseUnit.trim() && frequency.trim() && dose && doseValue > 0);

    const reset = () => {
        setMedicationId('');
        setDose('');
        setDoseUnit('');
        setRoute('');
        setFrequency('');
        setDurationDays('');
        setInstructions('');
    };

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        if (saving || !valid || !route) return;
        setSaving(true);
        try {
            await clinicalApi.createPrescription(encounter.id, {
                notes: null,
                details: [
                    {
                        medicationId,
                        dose: doseValue,
                        doseUnit: doseUnit.trim(),
                        route,
                        frequency: frequency.trim(),
                        durationDays: durationDays ? Number(durationDays) : null,
                        instructions: instructions.trim() || null,
                    },
                ],
            });
            feedback.success('prescripción emitida');
            reset();
            prescriptions.reload();
        } catch (error) {
            feedback.failure('No se pudo emitir la prescripción.', error);
        } finally {
            setSaving(false);
        }
    };

    return (
        <Card title="Prescripciones">
            <QueryView
                state={prescriptions.state}
                onRetry={prescriptions.reload}
                errorTitle="No se pudieron cargar las prescripciones."
                isEmpty={(list) => list.length === 0}
                empty={<EmptyState title="Sin prescripciones" />}
            >
                {(list) => (
                    <ul className="m-0 flex list-none flex-col gap-2 p-0">
                        {list.map((p) => (
                            <li key={p.id} className="flex flex-col gap-1.5 rounded-lg border border-line-soft p-3">
                                <span className="flex flex-wrap items-center justify-between gap-2 text-xs text-ink-muted">
                                    <Badge tone={p.status === 'Issued' ? 'info' : 'neutral'}>{PRESCRIPTION_STATUS[p.status]}</Badge>
                                    {formatDateTimeUtc(p.issuedAtUtc ?? p.createdAtUtc)}
                                </span>
                                {p.details.map((d) => (
                                    <span key={d.id} className="text-sm text-ink">
                                        <span className="font-semibold">{d.medicationName}</span> — {d.dose} {d.doseUnit}, {ROUTE_LABELS[d.route]},{' '}
                                        {d.frequency}
                                        {d.durationDays !== null && `, ${d.durationDays} días`}
                                        {d.instructions && <span className="block text-xs text-ink-muted">{d.instructions}</span>}
                                    </span>
                                ))}
                            </li>
                        ))}
                    </ul>
                )}
            </QueryView>

            {open && canPrescribe && (
                <form onSubmit={submit} className="mt-5 flex flex-col gap-3 border-t border-line-soft pt-4">
                    <h3 className="m-0 text-sm font-semibold text-ink">Nueva prescripción</h3>
                    {catalog.state.status === 'error' ? (
                        <QueryView state={catalog.state} onRetry={catalog.reload} errorTitle="No se pudo cargar el catálogo de medicamentos.">
                            {() => null}
                        </QueryView>
                    ) : (
                        <Select
                            label="Medicamento"
                            value={medicationId}
                            onChange={(e) => setMedicationId(e.target.value)}
                            options={medications.map((m) => ({
                                value: m.id,
                                label: [m.name, m.concentration, m.presentation].filter(Boolean).join(' · ') + (m.status === 'Inactive' ? ' (inactivo)' : ''),
                                disabled: m.status !== 'Active',
                            }))}
                            placeholder={catalog.state.status === 'loading' ? 'Cargando catálogo…' : 'Selecciona del catálogo…'}
                            disabled={catalog.state.status !== 'success'}
                            hint={selected?.defaultRoute ? `Vía habitual: ${ROUTE_LABELS[selected.defaultRoute]}` : undefined}
                            required
                        />
                    )}
                    <div className="grid gap-3 sm:grid-cols-3">
                        <Input label="Dosis" type="number" inputMode="decimal" min={0} step="any" value={dose} onChange={(e) => setDose(e.target.value)} required />
                        <Input label="Unidad" value={doseUnit} onChange={(e) => setDoseUnit(e.target.value)} placeholder="mg, mL, UI…" required maxLength={20} />
                        <Select
                            label="Vía"
                            value={route}
                            onChange={(e) => setRoute(e.target.value as MedicationRoute | '')}
                            options={(Object.keys(ROUTE_LABELS) as MedicationRoute[]).map((r) => ({ value: r, label: ROUTE_LABELS[r] }))}
                            placeholder="Selecciona…"
                            required
                        />
                    </div>
                    <div className="grid gap-3 sm:grid-cols-2">
                        <Input label="Frecuencia" value={frequency} onChange={(e) => setFrequency(e.target.value)} placeholder="Ej.: cada 8 horas" required maxLength={100} />
                        <Input
                            label="Duración en días (opcional)"
                            type="number"
                            inputMode="numeric"
                            min={1}
                            step={1}
                            value={durationDays}
                            onChange={(e) => setDurationDays(e.target.value)}
                        />
                    </div>
                    <Textarea label="Indicaciones (opcional)" value={instructions} onChange={(e) => setInstructions(e.target.value)} rows={2} />
                    <Button type="submit" loading={saving} loadingText="Emitiendo…" disabled={!valid} className="w-full sm:w-fit">
                        Emitir prescripción
                    </Button>
                </form>
            )}
        </Card>
    );
}

function AdministrationsCard({ encounterId }: { encounterId: string }) {
    const { state, reload } = useClinicalQuery(`administrations:${encounterId}`, (signal) =>
        clinicalApi.listMedicationAdministrations(encounterId, signal),
    );

    return (
        <Card title="Administración de medicamentos (Kardex)" subtitle="Registro independiente de la prescripción.">
            <QueryView
                state={state}
                onRetry={reload}
                errorTitle="No se pudo cargar el Kardex."
                isEmpty={(list) => list.length === 0}
                empty={<EmptyState title="Sin administraciones registradas" description="El registro de administraciones llegará con su contrato en el backend." />}
            >
                {(list) => (
                    <ul className="m-0 flex list-none flex-col gap-2 p-0">
                        {list.map((a) => (
                            <li key={a.id} className="flex flex-col gap-0.5 rounded-lg border border-line-soft p-3 text-sm">
                                <span className="font-semibold text-ink">
                                    {a.medicationName} — {a.dose} {a.doseUnit}, {ROUTE_LABELS[a.route]}
                                </span>
                                <span className="text-xs text-ink-muted">
                                    {formatDateTimeUtc(a.administeredAtUtc)}
                                    {a.status === 'EnteredInError' && ' · Registrado por error'}
                                </span>
                            </li>
                        ))}
                    </ul>
                )}
            </QueryView>
        </Card>
    );
}

function DischargeCard({ encounter, onClosed }: { encounter: ClinicalEncounterSummary; onClosed: () => void }) {
    const feedback = useClinicalFeedback();
    const [note, setNote] = useState('');
    const [confirming, setConfirming] = useState(false);
    const canClose = useAuth().can(Permissions.Encounters.Close);
    const closable = CLOSABLE_STATUSES.includes(encounter.status);

    const close = async () => {
        try {
            await clinicalApi.closeEncounter(encounter.id, { dischargeNote: note.trim() || null });
            feedback.success('atención cerrada');
            setConfirming(false);
            onClosed();
        } catch (error) {
            feedback.failure('No se pudo cerrar la atención.', error);
            setConfirming(false);
        }
    };

    if (!isEncounterOpen(encounter.status)) {
        return (
            <Card title="Egreso">
                <p className="m-0 text-sm text-ink-muted">
                    La atención está {ENCOUNTER_STATUS[encounter.status].label.toLowerCase()}
                    {encounter.endedAtUtc && ` desde ${formatDateTimeUtc(encounter.endedAtUtc)}`}.
                </p>
            </Card>
        );
    }

    return (
        <Card title="Egreso" subtitle="Cierra la atención. La nota se guarda como nota clínica de egreso.">
            <div className="flex flex-col gap-3">
                <Textarea label="Nota de egreso (opcional)" value={note} onChange={(e) => setNote(e.target.value)} rows={4} />
                {!closable && (
                    <p className="m-0 text-xs text-warning-700">
                        Una atención en estado «{ENCOUNTER_STATUS[encounter.status].label}» no puede cerrarse todavía.
                    </p>
                )}
                {!canClose && <p className="m-0 text-xs text-ink-muted">Cerrar la atención requiere el permiso Encounters.Close.</p>}
                <Button variant="danger" className="w-full sm:w-fit" disabled={!closable || !canClose} onClick={() => setConfirming(true)}>
                    Cerrar atención
                </Button>
            </div>
            <ConfirmDialog
                open={confirming}
                title="Cerrar la atención"
                message="La atención quedará cerrada y no admitirá nuevos registros clínicos. Esta acción no se puede deshacer."
                confirmLabel="Cerrar atención"
                onConfirm={close}
                onCancel={() => setConfirming(false)}
            />
        </Card>
    );
}
