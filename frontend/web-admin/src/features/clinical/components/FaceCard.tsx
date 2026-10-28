import { useCallback, useEffect, useRef, useState } from 'react';
import { ScanFace } from 'lucide-react';
import { Badge, Button, Card, ConfirmDialog, EmptyState, ErrorState, LoadingSpinner, Select } from '../../../components/shared';
import { clinicalApi, describeError } from '../api';
import type { FaceRevocationReason, FaceVerificationOutcome, Guid, PatientFace } from '../types/clinical.types';
import { CameraCapture } from './CameraCapture';

const formatDate = (value: string) => new Date(value).toLocaleString('es-DO', { dateStyle: 'medium', timeStyle: 'short' });

const PROVIDERS: Record<string, string> = { 'SCIPH-FaceFake': 'Proveedor simulado (desarrollo)' };
const providerLabel = (provider: string) => PROVIDERS[provider] ?? provider;

const REVOCATION_REASONS: { value: FaceRevocationReason; label: string }[] = [
    { value: 'PatientRequest', label: 'Solicitud del paciente' },
    { value: 'EnrollmentError', label: 'Error en el registro' },
    { value: 'ReferenceOutdated', label: 'Referencia desactualizada' },
    { value: 'Other', label: 'Otro' },
];

// Solo el resultado: nunca un porcentaje, una confianza ni un umbral.
const OUTCOMES: Record<FaceVerificationOutcome, { tone: 'success' | 'danger' | 'warning'; text: string }> = {
    Match: { tone: 'success', text: 'Coincide' },
    NoMatch: { tone: 'danger', text: 'No coincide' },
    NoFace: { tone: 'warning', text: 'No se detectó un rostro' },
    MultipleFaces: { tone: 'warning', text: 'Se detectaron varios rostros' },
    LowQuality: { tone: 'warning', text: 'Calidad insuficiente' },
};

type Loaded = { status: 'loading' } | { status: 'error'; error: string } | { status: 'ready'; faces: PatientFace[] };
type Mode = 'idle' | 'enroll' | 'verify';

/**
 * Rostro del paciente (ADR-012): verificación 1:1 ASISTIDA contra el rostro registrado de ESTE paciente, nunca una
 * búsqueda entre pacientes. Sin prueba de vida: ayuda al personal presente, no es una autenticación ni una prueba legal
 * de identidad. Registrar y revocar exigen Biometrics.Manage; verificar, Patients.Identify. Nunca se muestran la
 * referencia del proveedor, la plantilla, el puntaje ni el umbral.
 */
export function FaceCard({ patientId, canManage, canVerify }: { patientId: Guid; canManage: boolean; canVerify: boolean }) {
    const [loaded, setLoaded] = useState<Loaded>({ status: 'loading' });
    const [mode, setMode] = useState<Mode>('idle');
    const [busy, setBusy] = useState(false);
    const [message, setMessage] = useState<{ tone: 'success' | 'warning' | 'danger'; text: string } | null>(null);
    const [result, setResult] = useState<FaceVerificationOutcome | null>(null);
    const [revoking, setRevoking] = useState<PatientFace | null>(null);
    const [reason, setReason] = useState<FaceRevocationReason | ''>('');
    // Clave de la captura enviada: se conserva si se reintenta la MISMA foto y se renueva con cada foto nueva.
    const request = useRef<{ capture: Blob; id: string } | null>(null);

    const load = useCallback(async (signal?: AbortSignal) => {
        try {
            const faces = await clinicalApi.listPatientFaces(patientId, signal);
            setLoaded({ status: 'ready', faces });
        } catch (error) {
            if (signal?.aborted) return;
            setLoaded({ status: 'error', error: describeError(error) });
        }
    }, [patientId]);

    useEffect(() => {
        const controller = new AbortController();
        void load(controller.signal);
        return () => controller.abort();
    }, [load]);

    const keyFor = (capture: Blob) => {
        if (request.current?.capture !== capture) request.current = { capture, id: crypto.randomUUID() };
        return request.current.id;
    };

    const open = (next: Mode) => {
        setMessage(null);
        setResult(null);
        setMode(next);
    };

    const submit = async (capture: Blob) => {
        setBusy(true);
        setMessage(null);
        try {
            if (mode === 'enroll') {
                const enrolled = await clinicalApi.enrollPatientFace(patientId, capture, keyFor(capture));
                if (enrolled.outcome === 'Enrolled') {
                    setMessage({ tone: 'success', text: 'Rostro registrado.' });
                    setMode('idle');
                    void load();
                } else {
                    setMessage({ tone: 'warning', text: `${OUTCOMES[enrolled.outcome].text}. Toma la foto de nuevo.` });
                }
            } else {
                const verified = await clinicalApi.verifyPatientFace(patientId, capture, keyFor(capture));
                setResult(verified.outcome);
                setMode('idle');
            }
        } catch (error) {
            setMessage({ tone: 'danger', text: describeError(error) });
        } finally {
            setBusy(false);
        }
    };

    const revoke = async () => {
        const target = revoking;
        setRevoking(null);
        if (!target) return;
        setBusy(true);
        setMessage(null);
        setResult(null);
        try {
            const revoked = await clinicalApi.revokePatientFace(patientId, target.id, reason || undefined);
            setLoaded((current) =>
                current.status === 'ready' ? { ...current, faces: current.faces.map((f) => (f.id === revoked.id ? revoked : f)) } : current,
            );
            setMessage({ tone: 'success', text: 'Rostro revocado. Ya no se usa para verificar.' });
            void load();
        } catch (error) {
            setMessage({ tone: 'danger', text: describeError(error) });
        } finally {
            setBusy(false);
        }
    };

    const active = loaded.status === 'ready' ? loaded.faces.find((f) => f.status === 'Active') : undefined;

    return (
        <Card title="Rostro" subtitle="Verificación 1:1 asistida contra el rostro registrado de este paciente.">
            {loaded.status === 'loading' && <LoadingSpinner label="Cargando rostro…" />}
            {loaded.status === 'error' && <ErrorState variant="banner" title="No se pudo cargar el rostro." message={loaded.error} />}
            {loaded.status === 'ready' && (
                <div className="flex flex-col gap-4">
                    <p className="m-0 text-xs text-ink-muted">
                        Verificación asistida: ayuda al personal presente. No es una autenticación ni una prueba legal de identidad.
                    </p>
                    {loaded.faces.length === 0 ? (
                        <EmptyState icon={ScanFace} title="Sin rostro registrado" description="El paciente aún no tiene un rostro registrado." />
                    ) : (
                        <ul aria-label="Rostros registrados" className="m-0 flex list-none flex-col gap-2 p-0">
                            {loaded.faces.map((f) => (
                                <li key={f.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-line p-3">
                                    <span className="flex flex-col gap-0.5">
                                        <span className="font-semibold text-ink">{providerLabel(f.provider)}</span>
                                        <span className="text-xs text-ink-muted">
                                            Registrado el {formatDate(f.registeredAtUtc)}
                                            {f.revokedAtUtc && ` · revocado el ${formatDate(f.revokedAtUtc)}`}
                                        </span>
                                    </span>
                                    <span className="flex flex-wrap items-center gap-2">
                                        <Badge tone={f.status === 'Active' ? 'success' : 'neutral'}>{f.status === 'Active' ? 'Activo' : 'Revocado'}</Badge>
                                        {f.status === 'Active' && canManage && (
                                            <Button variant="secondary" size="sm" disabled={busy || mode !== 'idle'} onClick={() => { setReason(''); setRevoking(f); }}>
                                                Revocar rostro
                                            </Button>
                                        )}
                                    </span>
                                </li>
                            ))}
                        </ul>
                    )}

                    {result && (
                        <div role="status" aria-label="Resultado de la verificación" className="flex items-center gap-2">
                            <Badge tone={OUTCOMES[result].tone}>{OUTCOMES[result].text}</Badge>
                        </div>
                    )}
                    {message && (
                        <p role="status" className={message.tone === 'success' ? 'text-sm text-success-600' : message.tone === 'warning' ? 'text-sm text-warning-700' : 'text-sm text-danger-text'}>
                            {message.text}
                        </p>
                    )}

                    {mode !== 'idle' ? (
                        <CameraCapture
                            confirmLabel={mode === 'enroll' ? 'Registrar este rostro' : 'Verificar este rostro'}
                            busy={busy}
                            onCapture={(capture) => void submit(capture)}
                            onCancel={() => setMode('idle')}
                        />
                    ) : (
                        <div className="flex flex-wrap gap-2">
                            {active && canVerify && (
                                <Button leftIcon={<ScanFace size={16} aria-hidden="true" />} disabled={busy} onClick={() => open('verify')}>
                                    Verificar rostro
                                </Button>
                            )}
                            {!active && canManage && (
                                <Button leftIcon={<ScanFace size={16} aria-hidden="true" />} disabled={busy} onClick={() => open('enroll')}>
                                    Registrar rostro
                                </Button>
                            )}
                        </div>
                    )}
                </div>
            )}
            <ConfirmDialog
                open={revoking !== null}
                tone="danger"
                title="Revocar rostro"
                message={
                    <div className="flex flex-col gap-3">
                        <p className="m-0">El rostro dejará de usarse para verificar a este paciente de inmediato.</p>
                        <Select
                            label="Motivo (opcional)"
                            value={reason}
                            onChange={(e) => setReason(e.target.value as FaceRevocationReason | '')}
                            placeholder="Sin motivo"
                            options={REVOCATION_REASONS}
                        />
                    </div>
                }
                confirmLabel="Revocar"
                onConfirm={revoke}
                onCancel={() => setRevoking(null)}
            />
        </Card>
    );
}
