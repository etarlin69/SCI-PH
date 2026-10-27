import { useCallback, useEffect, useRef, useState } from 'react';
import { Fingerprint } from 'lucide-react';
import { Badge, Button, Card, ConfirmDialog, EmptyState, ErrorState, LoadingSpinner, Select } from '../../../components/shared';
import { clinicalApi, describeError } from '../api';
import type { FingerprintEnroller, FingerprintEnrollment, Guid, PatientFingerprint } from '../types/clinical.types';

/** Cada cuánto se consulta un registro en curso. */
export const ENROLLMENT_POLL_MS = 1000;

const formatDate = (value: string) => new Date(value).toLocaleString('es-DO', { dateStyle: 'medium', timeStyle: 'short' });

const ENDED_MESSAGES: Record<'Failed' | 'LowQuality' | 'SensorError' | 'Expired' | 'Cancelled', string> = {
    Failed: 'El lector no pudo registrar la huella. Puedes intentarlo de nuevo.',
    LowQuality: 'La lectura de la huella no fue suficiente. Intenta de nuevo.',
    SensorError: 'El lector no está disponible. Revisa el lector e intenta de nuevo.',
    Expired: 'El registro venció sin respuesta del lector. Intenta de nuevo.',
    Cancelled: 'Registro cancelado.',
};

type Loaded = { status: 'loading' } | { status: 'error'; error: string } | { status: 'ready'; fingerprints: PatientFingerprint[]; enrollers: FingerprintEnroller[] };

/**
 * Huella digital del paciente (ADR-010): referencias registradas (lector y fecha; nunca el slot ni la referencia interna)
 * y registro de una huella nueva en un lector que registra e identifica. El lector guarda la plantilla en su sensor;
 * SCI-PH solo guarda la referencia. Mientras el registro está pendiente se consulta cada segundo; la consulta se detiene
 * al terminar o al salir de la pantalla. No es un registro formal de consentimiento.
 */
export function FingerprintCard({ patientId }: { patientId: Guid }) {
    const [loaded, setLoaded] = useState<Loaded>({ status: 'loading' });
    const [readerId, setReaderId] = useState('');
    const [confirming, setConfirming] = useState(false);
    const [enrollment, setEnrollment] = useState<FingerprintEnrollment | null>(null);
    const [message, setMessage] = useState<{ tone: 'success' | 'warning' | 'danger'; text: string } | null>(null);
    const [busy, setBusy] = useState(false);
    // Clave del intento: se conserva en los reintentos de la apertura y se renueva al terminar el registro.
    const requestId = useRef<string | null>(null);

    const load = useCallback(async (signal?: AbortSignal) => {
        try {
            const [fingerprints, enrollers] = await Promise.all([
                clinicalApi.listPatientFingerprints(patientId, signal),
                clinicalApi.listFingerprintEnrollers(signal),
            ]);
            setLoaded({ status: 'ready', fingerprints, enrollers });
            setReaderId((current) => current || (enrollers.length === 1 ? enrollers[0].id : ''));
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

    const finish = useCallback(
        (ended: FingerprintEnrollment) => {
            setEnrollment(null);
            requestId.current = null;
            if (ended.status === 'Enrolled') {
                setMessage({ tone: 'success', text: 'Huella registrada. Ya puede usarse para identificar al paciente con ese lector.' });
                void load();
            } else if (ended.status !== 'Pending') {
                const key = ended.status === 'Failed' ? (ended.failure ?? 'Failed') : ended.status;
                setMessage({ tone: ended.status === 'Cancelled' ? 'warning' : 'danger', text: ENDED_MESSAGES[key] });
            }
        },
        [load],
    );

    // Consulta del registro pendiente: se detiene al cambiar de estado y al desmontar (cambio de ruta).
    const pendingId = enrollment?.status === 'Pending' ? enrollment.id : null;
    useEffect(() => {
        if (!pendingId) return;
        const controller = new AbortController();
        const timer = setInterval(async () => {
            try {
                const current = await clinicalApi.getFingerprintEnrollment(patientId, pendingId, controller.signal);
                if (controller.signal.aborted) return;
                if (current.status === 'Pending') setEnrollment(current);
                else finish(current);
            } catch (error) {
                if (controller.signal.aborted) return;
                setMessage({ tone: 'warning', text: `No se pudo consultar el registro: ${describeError(error)}` });
            }
        }, ENROLLMENT_POLL_MS);
        return () => {
            clearInterval(timer);
            controller.abort();
        };
    }, [pendingId, patientId, finish]);

    const start = async () => {
        setConfirming(false);
        setBusy(true);
        setMessage(null);
        try {
            requestId.current ??= crypto.randomUUID();
            const opened = await clinicalApi.openFingerprintEnrollment(patientId, { clientRequestId: requestId.current, deviceId: readerId });
            if (opened.status === 'Pending') setEnrollment(opened);
            else finish(opened);
        } catch (error) {
            setMessage({ tone: 'danger', text: describeError(error) });
        } finally {
            setBusy(false);
        }
    };

    const cancel = async () => {
        if (!enrollment) return;
        setBusy(true);
        try {
            finish(await clinicalApi.cancelFingerprintEnrollment(patientId, enrollment.id));
        } catch (error) {
            setMessage({ tone: 'danger', text: describeError(error) });
        } finally {
            setBusy(false);
        }
    };

    const selectReader = (value: string) => {
        setReaderId(value);
        requestId.current = null;
    };

    return (
        <Card title="Huella digital" subtitle="El lector guarda la huella; SCI-PH solo guarda a qué paciente pertenece.">
            {loaded.status === 'loading' && <LoadingSpinner label="Cargando huellas…" />}
            {loaded.status === 'error' && <ErrorState variant="banner" title="No se pudieron cargar las huellas." message={loaded.error} />}
            {loaded.status === 'ready' && (
                <div className="flex flex-col gap-4">
                    {loaded.fingerprints.length === 0 ? (
                        <EmptyState icon={Fingerprint} title="Sin huella registrada" description="El paciente aún no tiene huellas registradas en ningún lector." />
                    ) : (
                        <ul aria-label="Huellas registradas" className="m-0 flex list-none flex-col gap-2 p-0">
                            {loaded.fingerprints.map((f) => (
                                <li key={f.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-line p-3">
                                    <span className="flex flex-col gap-0.5">
                                        <span className="font-semibold text-ink">{f.reader ? `${f.reader.name} (${f.reader.code})` : 'Lector desconocido'}</span>
                                        <span className="text-xs text-ink-muted">
                                            Registrada el {formatDate(f.registeredAtUtc)}
                                            {f.revokedAtUtc && ` · revocada el ${formatDate(f.revokedAtUtc)}`}
                                        </span>
                                    </span>
                                    <span className="flex gap-2">
                                        <Badge tone={f.status === 'Active' ? 'success' : 'neutral'}>{f.status === 'Active' ? 'Activa' : 'Revocada'}</Badge>
                                        {f.status === 'Active' && f.reader && !f.reader.available && <Badge tone="warning">Lector no disponible</Badge>}
                                    </span>
                                </li>
                            ))}
                        </ul>
                    )}

                    {message && (
                        <p role="status" className={message.tone === 'success' ? 'text-sm text-success-600' : message.tone === 'warning' ? 'text-sm text-warning-700' : 'text-sm text-danger-text'}>
                            {message.text}
                        </p>
                    )}

                    {enrollment ? (
                        <div className="flex flex-col gap-3 rounded-lg border border-line p-3" aria-live="polite">
                            <LoadingSpinner label="Esperando al lector: pide al paciente que coloque el dedo." />
                            <div>
                                <Button variant="secondary" onClick={cancel} loading={busy}>
                                    Cancelar registro
                                </Button>
                            </div>
                        </div>
                    ) : loaded.enrollers.length === 0 ? (
                        <p className="text-sm text-ink-muted">No hay lectores disponibles para registrar huellas.</p>
                    ) : (
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
                            <Select
                                label="Lector"
                                value={readerId}
                                onChange={(e) => selectReader(e.target.value)}
                                placeholder="Elige un lector"
                                options={loaded.enrollers.map((r) => ({ value: r.id, label: `${r.name} (${r.code})` }))}
                                containerClassName="min-w-0 flex-1"
                            />
                            <Button leftIcon={<Fingerprint size={16} aria-hidden="true" />} disabled={!readerId} loading={busy} onClick={() => setConfirming(true)}>
                                Registrar huella
                            </Button>
                        </div>
                    )}
                </div>
            )}
            <ConfirmDialog
                open={confirming}
                tone="primary"
                title="Registrar huella"
                message="Confirme que desea registrar la huella de este paciente en el lector elegido."
                confirmLabel="Registrar"
                onConfirm={start}
                onCancel={() => setConfirming(false)}
            />
        </Card>
    );
}
