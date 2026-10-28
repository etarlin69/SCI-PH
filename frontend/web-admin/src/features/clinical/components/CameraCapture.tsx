import { useCallback, useEffect, useRef, useState } from 'react';
import { Camera, RotateCcw } from 'lucide-react';
import { Button } from '../../../components/shared';

export interface CameraCaptureProps {
    /** Texto del botón que envía la foto tomada. */
    confirmLabel: string;
    busy?: boolean;
    onCapture: (capture: Blob) => void;
    onCancel: () => void;
}

type CameraState = { status: 'starting' } | { status: 'live' } | { status: 'captured'; blob: Blob; url: string } | { status: 'error'; message: string };

const describeCameraError = (error: unknown) =>
    error instanceof DOMException && (error.name === 'NotAllowedError' || error.name === 'SecurityError')
        ? 'No se pudo usar la cámara: el permiso fue denegado. Permite el acceso a la cámara en el navegador e inténtalo de nuevo.'
        : 'No hay una cámara disponible en este equipo.';

/**
 * Toma UNA foto con la cámara del equipo (getUserMedia); nunca ofrece elegir un archivo. La cámara se apaga en cuanto se
 * toma la foto, al cancelar y al salir. La vista previa vive solo en memoria (URL de objeto que se libera); la foto no se
 * guarda en el navegador (ni localStorage, ni sessionStorage, ni IndexedDB).
 */
export function CameraCapture({ confirmLabel, busy = false, onCapture, onCancel }: CameraCaptureProps) {
    const video = useRef<HTMLVideoElement>(null);
    const stream = useRef<MediaStream | null>(null);
    const [state, setState] = useState<CameraState>({ status: 'starting' });

    const stop = useCallback(() => {
        stream.current?.getTracks().forEach((track) => track.stop());
        stream.current = null;
    }, []);

    const start = useCallback(async () => {
        setState({ status: 'starting' });
        if (!navigator.mediaDevices?.getUserMedia) {
            setState({ status: 'error', message: describeCameraError(null) });
            return;
        }
        try {
            const media = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: 'user', width: { ideal: 1280 }, height: { ideal: 720 } },
                audio: false,
            });
            stream.current = media;
            if (video.current) {
                video.current.srcObject = media;
                await video.current.play().catch(() => undefined);
            }
            setState({ status: 'live' });
        } catch (error) {
            setState({ status: 'error', message: describeCameraError(error) });
        }
    }, []);

    useEffect(() => {
        void start();
        return stop;
    }, [start, stop]);

    // La vista previa se libera al cambiar de foto o al salir.
    const previewUrl = state.status === 'captured' ? state.url : null;
    useEffect(() => () => {
        if (previewUrl) URL.revokeObjectURL(previewUrl);
    }, [previewUrl]);

    const take = () => {
        const source = video.current;
        if (!source) return;
        const canvas = document.createElement('canvas');
        canvas.width = source.videoWidth || 1280;
        canvas.height = source.videoHeight || 720;
        canvas.getContext('2d')?.drawImage(source, 0, 0, canvas.width, canvas.height);
        canvas.toBlob(
            (blob) => {
                stop();
                if (!blob) {
                    setState({ status: 'error', message: 'No se pudo tomar la foto. Inténtalo de nuevo.' });
                    return;
                }
                setState({ status: 'captured', blob, url: URL.createObjectURL(blob) });
            },
            'image/jpeg',
            0.92,
        );
    };

    const cancel = () => {
        stop();
        onCancel();
    };

    return (
        <div className="flex flex-col gap-3 rounded-lg border border-line p-3" aria-label="Cámara">
            {state.status === 'error' ? (
                <p role="alert" className="text-sm text-danger-text">
                    {state.message}
                </p>
            ) : state.status === 'captured' ? (
                <img src={state.url} alt="Foto tomada" className="max-h-64 w-full rounded-md object-contain" />
            ) : (
                <video ref={video} aria-label="Vista de la cámara" muted playsInline className="max-h-64 w-full rounded-md bg-black object-contain" />
            )}
            <div className="flex flex-wrap gap-2">
                {state.status === 'live' && (
                    <Button leftIcon={<Camera size={16} aria-hidden="true" />} onClick={take}>
                        Tomar foto
                    </Button>
                )}
                {state.status === 'captured' && (
                    <>
                        <Button loading={busy} onClick={() => onCapture(state.blob)}>
                            {confirmLabel}
                        </Button>
                        <Button variant="secondary" leftIcon={<RotateCcw size={16} aria-hidden="true" />} disabled={busy} onClick={() => void start()}>
                            Repetir
                        </Button>
                    </>
                )}
                <Button variant="secondary" disabled={busy} onClick={cancel}>
                    Cancelar
                </Button>
            </div>
        </div>
    );
}
