import { useState, type FormEvent } from 'react';
import { Permissions, useAuth } from '../../../auth';
import { useParams } from 'react-router-dom';
import { Badge, Button, Card, ConfirmDialog, EmptyState, Input, Select, Textarea } from '../../../components/shared';
import { clinicalApi } from '../api';
import { EncounterShell } from '../components/EncounterShell';
import { QueryView } from '../components/QueryView';
import { SimulatedBadge } from '../components/SimulatedBadge';
import { useClinicalFeedback } from '../hooks/useClinicalFeedback';
import { useClinicalQuery, type QueryState } from '../hooks/useClinicalQuery';
import { useClinicalTime } from '../hooks/useClinicalTime';
import type {
    ClinicalEncounterSummary,
    Medication,
    MedicationAdministration,
    MedicationRoute,
    Prescription,
    PrescriptionDetail,
    PrescriptionStatus,
} from '../types/clinical.types';
import {
    CLOSABLE_STATUSES,
    ENCOUNTER_STATUS,
    ROUTE_LABELS,
    acceptsClinicalRecords,
    startsCare,
    formatDateTimeUtc,
    isEncounterOpen,
} from '../utils/clinicalLabels';

type Query<T> = { state: QueryState<T>; reload: () => void };

/**
 * /hospital/atenciones/:id/medicacion: prescripción, Kardex y egreso de la atención. El Kardex no es un dato propio:
 * es la vista de las prescripciones emitidas (con sus líneas) y de las administraciones registradas en el backend.
 */
export function PharmacyAndDischargePage() {
    const { encounterId } = useParams();
    if (!encounterId) return null;

    return (
        <EncounterShell encounterId={encounterId}>
            {(encounter, reloadEncounter) => <EncounterMedication encounter={encounter} onClosed={reloadEncounter} onCareStarted={reloadEncounter} />}
        </EncounterShell>
    );
}

function EncounterMedication({
    encounter,
    onClosed,
    onCareStarted,
}: {
    encounter: ClinicalEncounterSummary;
    onClosed: () => void;
    onCareStarted: () => void;
}) {
    const { can } = useAuth();
    const writable = acceptsClinicalRecords(encounter.status);
    const canPrescribe = can(Permissions.Prescriptions.Write);
    const canAdminister = can(Permissions.MedicationAdministrations.Write);
    // El primer registro en esta clínica empieza la atención (Created/Received → InProgress): se recarga una vez.
    const onRecorded = () => {
        if (startsCare(encounter.status)) onCareStarted();
    };
    // Una sola consulta por recurso, compartida por las tarjetas: lo que se emite aparece también en el Kardex.
    const prescriptions = useClinicalQuery(`prescriptions:${encounter.id}`, (signal) => clinicalApi.listPrescriptions(encounter.id, signal));
    const administrations = useClinicalQuery(`administrations:${encounter.id}`, (signal) =>
        clinicalApi.listMedicationAdministrations(encounter.id, signal),
    );
    // El catálogo solo hace falta para escribir.
    const catalog = useClinicalQuery(
        writable && can(Permissions.Medications.Read) && (canPrescribe || canAdminister) ? 'medications' : null,
        (signal) => clinicalApi.listMedications(signal),
    );

    return (
        <div className="grid gap-5 xl:grid-cols-2">
            <PrescriptionsCard
                encounterId={encounter.id}
                prescriptions={prescriptions}
                catalog={catalog}
                canWrite={writable && canPrescribe}
                onRecorded={onRecorded}
            />
            <div className="flex flex-col gap-5">
                <KardexCard
                    encounterId={encounter.id}
                    prescriptions={prescriptions}
                    administrations={administrations}
                    catalog={catalog}
                    canWrite={writable && canAdminister}
                    onRecorded={onRecorded}
                />
                <DischargeCard encounter={encounter} onClosed={onClosed} />
            </div>
        </div>
    );
}

const PRESCRIPTION_STATUS: Record<PrescriptionStatus, string> = { Draft: 'Borrador', Issued: 'Emitida', Cancelled: 'Anulada' };

const ROUTE_OPTIONS = (Object.keys(ROUTE_LABELS) as MedicationRoute[]).map((r) => ({ value: r, label: ROUTE_LABELS[r] }));

const medicationLabel = (m: Medication) =>
    [m.name, m.concentration, m.presentation].filter(Boolean).join(' · ') + (m.status === 'Inactive' ? ' (inactivo)' : '');

const describeLine = (d: PrescriptionDetail) =>
    `${d.dose} ${d.doseUnit}, ${ROUTE_LABELS[d.route]}, ${d.frequency}${d.durationDays !== null ? `, ${d.durationDays} días` : ''}`;

function CatalogSelect({
    catalog,
    value,
    onChange,
}: {
    catalog: Query<Medication[]>;
    value: string;
    onChange: (medication: Medication | undefined) => void;
}) {
    if (catalog.state.status === 'error') {
        return (
            <QueryView state={catalog.state} onRetry={catalog.reload} errorTitle="No se pudo cargar el catálogo de medicamentos.">
                {() => null}
            </QueryView>
        );
    }
    const medications = catalog.state.status === 'success' ? catalog.state.data : [];
    const selected = medications.find((m) => m.id === value);
    return (
        <Select
            label="Medicamento"
            value={value}
            onChange={(e) => onChange(medications.find((m) => m.id === e.target.value))}
            options={medications.map((m) => ({ value: m.id, label: medicationLabel(m), disabled: m.status !== 'Active' }))}
            placeholder={catalog.state.status === 'loading' ? 'Cargando catálogo…' : 'Selecciona del catálogo…'}
            disabled={catalog.state.status !== 'success'}
            hint={selected?.defaultRoute ? `Vía habitual: ${ROUTE_LABELS[selected.defaultRoute]}` : undefined}
            required
        />
    );
}

function PrescriptionsCard({
    encounterId,
    prescriptions,
    catalog,
    canWrite,
    onRecorded,
}: {
    encounterId: string;
    prescriptions: Query<Prescription[]>;
    catalog: Query<Medication[]>;
    canWrite: boolean;
    onRecorded: () => void;
}) {
    const feedback = useClinicalFeedback('prescriptions');
    const [medicationId, setMedicationId] = useState('');
    const [dose, setDose] = useState('');
    const [doseUnit, setDoseUnit] = useState('');
    const [route, setRoute] = useState<MedicationRoute | ''>('');
    const [frequency, setFrequency] = useState('');
    const [durationDays, setDurationDays] = useState('');
    const [instructions, setInstructions] = useState('');
    const [saving, setSaving] = useState(false);

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
            await clinicalApi.createPrescription(encounterId, {
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
            onRecorded();
        } catch (error) {
            feedback.failure('No se pudo emitir la prescripción.', error);
        } finally {
            setSaving(false);
        }
    };

    return (
        <Card title="Prescripciones" actions={<SimulatedBadge feature="prescriptions" />}>
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
                                        <span className="font-semibold">{d.medicationName}</span> — {describeLine(d)}
                                        {d.instructions && <span className="block text-xs text-ink-muted">{d.instructions}</span>}
                                    </span>
                                ))}
                                {p.notes && <span className="text-xs text-ink-muted">{p.notes}</span>}
                            </li>
                        ))}
                    </ul>
                )}
            </QueryView>

            {canWrite && (
                <form onSubmit={submit} className="mt-5 flex flex-col gap-3 border-t border-line-soft pt-4">
                    <h3 className="m-0 text-sm font-semibold text-ink">Nueva prescripción</h3>
                    <CatalogSelect
                        catalog={catalog}
                        value={medicationId}
                        onChange={(m) => {
                            setMedicationId(m?.id ?? '');
                            if (m?.defaultRoute && !route) setRoute(m.defaultRoute);
                        }}
                    />
                    <div className="grid gap-3 sm:grid-cols-3">
                        <Input label="Dosis" type="number" inputMode="decimal" min={0} step="any" value={dose} onChange={(e) => setDose(e.target.value)} required />
                        <Input label="Unidad" value={doseUnit} onChange={(e) => setDoseUnit(e.target.value)} placeholder="mg, mL, UI…" required maxLength={20} />
                        <Select
                            label="Vía"
                            value={route}
                            onChange={(e) => setRoute(e.target.value as MedicationRoute | '')}
                            options={ROUTE_OPTIONS}
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

// Valor del selector de línea para una dosis fuera de prescripción.
const UNPRESCRIBED = 'none';

function KardexCard({
    encounterId,
    prescriptions,
    administrations,
    catalog,
    canWrite,
    onRecorded,
}: {
    encounterId: string;
    prescriptions: Query<Prescription[]>;
    administrations: Query<MedicationAdministration[]>;
    catalog: Query<Medication[]>;
    canWrite: boolean;
    onRecorded: () => void;
}) {
    // Líneas de prescripciones emitidas: son las que admiten administraciones.
    const lines =
        prescriptions.state.status === 'success'
            ? prescriptions.state.data.filter((p) => p.status === 'Issued').flatMap((p) => p.details.map((d) => ({ prescription: p, detail: d })))
            : [];
    const given = administrations.state.status === 'success' ? administrations.state.data : [];

    return (
        <Card
            title="Administración de medicamentos (Kardex)"
            subtitle="Tratamiento prescrito y dosis administradas, tal como están registrados."
            actions={<SimulatedBadge feature="administrations" />}
        >
            {lines.length > 0 && (
                <section aria-label="Tratamiento prescrito" className="mb-4 flex flex-col gap-2">
                    <h3 className="m-0 text-sm font-semibold text-ink">Tratamiento prescrito</h3>
                    <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
                        {lines.map(({ detail }) => {
                            const doses = given.filter((a) => a.prescriptionDetailId === detail.id && a.status === 'Completed');
                            return (
                                <li key={detail.id} className="flex flex-col gap-0.5 rounded-lg bg-surface-subtle px-3 py-2 text-sm">
                                    <span className="text-ink">
                                        <span className="font-semibold">{detail.medicationName}</span> — {describeLine(detail)}
                                    </span>
                                    <span className="text-xs text-ink-muted">
                                        {doses.length === 0
                                            ? 'Sin dosis administradas'
                                            : `${doses.length} ${doses.length === 1 ? 'dosis administrada' : 'dosis administradas'} · última ${formatDateTimeUtc(doses[0].administeredAtUtc)}`}
                                    </span>
                                </li>
                            );
                        })}
                    </ul>
                </section>
            )}

            <QueryView
                state={administrations.state}
                onRetry={administrations.reload}
                errorTitle="No se pudo cargar el Kardex."
                isEmpty={(list) => list.length === 0}
                empty={<EmptyState title="Sin administraciones registradas" />}
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
                                    {a.prescriptionId === null && ' · Sin prescripción'}
                                    {a.status === 'EnteredInError' && ' · Registrado por error'}
                                </span>
                                {a.notes && <span className="text-xs text-ink-muted">{a.notes}</span>}
                            </li>
                        ))}
                    </ul>
                )}
            </QueryView>

            {canWrite && (
                <AdministrationForm
                    encounterId={encounterId}
                    lines={lines}
                    catalog={catalog}
                    onSaved={() => {
                        administrations.reload();
                        onRecorded();
                    }}
                />
            )}
        </Card>
    );
}

function AdministrationForm({
    encounterId,
    lines,
    catalog,
    onSaved,
}: {
    encounterId: string;
    lines: { prescription: Prescription; detail: PrescriptionDetail }[];
    catalog: Query<Medication[]>;
    onSaved: () => void;
}) {
    const feedback = useClinicalFeedback('administrations');
    const [lineId, setLineId] = useState('');
    const [medicationId, setMedicationId] = useState('');
    const [dose, setDose] = useState('');
    const [doseUnit, setDoseUnit] = useState('');
    const [route, setRoute] = useState<MedicationRoute | ''>('');
    const [notes, setNotes] = useState('');
    const administeredAt = useClinicalTime();
    const [saving, setSaving] = useState(false);

    const line = lines.find((l) => l.detail.id === lineId);
    const doseValue = Number(dose);
    const valid = Boolean(lineId && (line || medicationId) && route && doseUnit.trim() && dose && doseValue > 0);

    // Una línea prescrita fija el medicamento y propone su dosis, unidad y vía.
    const chooseLine = (value: string) => {
        setLineId(value);
        const chosen = lines.find((l) => l.detail.id === value);
        setMedicationId(chosen?.detail.medicationId ?? '');
        setDose(chosen ? String(chosen.detail.dose) : '');
        setDoseUnit(chosen?.detail.doseUnit ?? '');
        setRoute(chosen?.detail.route ?? '');
    };

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        if (saving || !valid || !route) return;
        setSaving(true);
        try {
            await clinicalApi.createMedicationAdministration(encounterId, {
                medicationId: line ? line.detail.medicationId : medicationId,
                dose: doseValue,
                doseUnit: doseUnit.trim(),
                route,
                administeredAtUtc: administeredAt.toUtc(),
                prescriptionId: line?.prescription.id ?? null,
                prescriptionDetailId: line?.detail.id ?? null,
                notes: notes.trim() || null,
            });
            feedback.success('administración registrada');
            chooseLine('');
            setNotes('');
            administeredAt.reset();
            onSaved();
        } catch (error) {
            feedback.failure('No se pudo registrar la administración.', error);
        } finally {
            setSaving(false);
        }
    };

    return (
        <form onSubmit={submit} className="mt-5 flex flex-col gap-3 border-t border-line-soft pt-4">
            <h3 className="m-0 text-sm font-semibold text-ink">Registrar administración</h3>
            <Select
                label="Línea de tratamiento"
                value={lineId}
                onChange={(e) => chooseLine(e.target.value)}
                options={[
                    ...lines.map(({ detail }) => ({ value: detail.id, label: `${detail.medicationName} — ${describeLine(detail)}` })),
                    { value: UNPRESCRIBED, label: 'Sin prescripción (medicamento del catálogo)' },
                ]}
                placeholder="Selecciona…"
                required
            />
            {lineId === UNPRESCRIBED && (
                <CatalogSelect
                    catalog={catalog}
                    value={medicationId}
                    onChange={(m) => {
                        setMedicationId(m?.id ?? '');
                        if (m?.defaultRoute && !route) setRoute(m.defaultRoute);
                    }}
                />
            )}
            <div className="grid gap-3 sm:grid-cols-3">
                <Input label="Dosis administrada" type="number" inputMode="decimal" min={0} step="any" value={dose} onChange={(e) => setDose(e.target.value)} required />
                <Input label="Unidad" value={doseUnit} onChange={(e) => setDoseUnit(e.target.value)} placeholder="mg, mL, UI…" required maxLength={20} />
                <Select
                    label="Vía"
                    value={route}
                    onChange={(e) => setRoute(e.target.value as MedicationRoute | '')}
                    options={ROUTE_OPTIONS}
                    placeholder="Selecciona…"
                    required
                />
            </div>
            <Input
                label="Fecha y hora de administración"
                type="datetime-local"
                value={administeredAt.value}
                onChange={(e) => administeredAt.change(e.target.value)}
                required
            />
            <Textarea label="Observaciones (opcional)" value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} />
            <Button type="submit" loading={saving} loadingText="Registrando…" disabled={!valid} className="w-full sm:w-fit">
                Registrar administración
            </Button>
        </form>
    );
}

function DischargeCard({ encounter, onClosed }: { encounter: ClinicalEncounterSummary; onClosed: () => void }) {
    const feedback = useClinicalFeedback('discharge');
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
        <Card
            title="Egreso"
            subtitle="Cierra la atención. La nota se guarda como nota clínica de egreso."
            actions={<SimulatedBadge feature="discharge" />}
        >
            <div className="flex flex-col gap-3">
                <Textarea label="Nota de egreso (opcional)" value={note} onChange={(e) => setNote(e.target.value)} rows={4} />
                {!closable && (
                    <p className="m-0 text-xs text-warning-700">
                        Una atención en estado «{ENCOUNTER_STATUS[encounter.status].label}» no puede cerrarse todavía.
                    </p>
                )}
                {/* La acción solo existe con el permiso real; el backend sigue siendo la autoridad (403/409). */}
                {canClose ? (
                    <Button variant="danger" className="w-full sm:w-fit" disabled={!closable} onClick={() => setConfirming(true)}>
                        Cerrar atención
                    </Button>
                ) : (
                    <p className="m-0 text-xs text-ink-muted">Cerrar la atención requiere el permiso Encounters.Close.</p>
                )}
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
