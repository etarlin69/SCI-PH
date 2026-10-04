import { useEffect, useState } from 'react';
import { Clock, TimerOff } from 'lucide-react';
import type { DeviceEnrollment } from '../../../types';
import { ENROLLMENT_TTL_MINUTES } from '../../../services';
import { cn } from '../../../utils/cn';
import { formatCountdown } from '../helpers';
import { OneTimeSecret } from './OneTimeSecret';
import { QrCode } from './QrCode';

export interface EnrollmentPanelProps {
  enrollment: DeviceEnrollment;
  expiredHint: string;
}

const secondsLeft = (expiresAt: number) => Math.max(0, Math.ceil((expiresAt - Date.now()) / 1000));

/** Panel de vinculación: QR real + secreto de un solo uso + cuenta atrás de `ENROLLMENT_TTL_MINUTES`. */
export function EnrollmentPanel({ enrollment, expiredHint }: EnrollmentPanelProps) {
  const [left, setLeft] = useState(() => secondsLeft(enrollment.expiresAt));
  const expired = left === 0;

  useEffect(() => {
    setLeft(secondsLeft(enrollment.expiresAt));
    const t = window.setInterval(() => {
      const s = secondsLeft(enrollment.expiresAt);
      setLeft(s);
      if (s === 0) window.clearInterval(t);
    }, 1000);
    return () => window.clearInterval(t);
  }, [enrollment.expiresAt]);

  // Anuncio para lectores de pantalla solo en hitos (no cada segundo).
  const announcement = expired
    ? 'El código de vinculación ha caducado.'
    : left <= 60
      ? 'Queda menos de un minuto para que caduque el código.'
      : '';

  return (
    <div className="flex flex-col gap-5 sm:flex-row sm:items-start">
      <div className="flex flex-col items-center gap-2 self-center sm:self-start">
        <div className="rounded-lg border border-line bg-white p-1.5">
          {expired ? (
            <div className="flex h-[184px] w-[184px] flex-col items-center justify-center gap-2 bg-surface-subtle p-4 text-center text-[13px] text-ink-muted">
              <TimerOff size={28} aria-hidden="true" />
              Código caducado
            </div>
          ) : (
            <QrCode value={enrollment.qrPayload} label={`Código QR de vinculación de ${enrollment.device.nombre}`} />
          )}
        </div>
        <span
          className={cn(
            'tabular inline-flex items-center gap-1.5 text-[13px] font-semibold',
            left > 120 ? 'text-ink-muted' : 'text-danger-text',
          )}
        >
          <Clock size={14} aria-hidden="true" />
          {expired ? 'Código caducado' : `Caduca en ${formatCountdown(left)}`}
        </span>
        <span className="sr-only" role="status" aria-live="polite">
          {announcement}
        </span>
      </div>
      <div className="flex min-w-0 flex-1 flex-col gap-3">
        <p className="text-[13px] leading-relaxed text-ink-3">
          Escanea el código con la app SCI-PH o copia el secreto en el dispositivo. El código es válido durante {ENROLLMENT_TTL_MINUTES}{' '}
          minutos.
        </p>
        <OneTimeSecret secret={enrollment.secret} expired={expired} />
        {expired && (
          <p role="alert" className="rounded-lg border border-danger-600/30 bg-danger-50 px-3 py-2 text-[13px] text-danger-text">
            {expiredHint}
          </p>
        )}
      </div>
    </div>
  );
}
