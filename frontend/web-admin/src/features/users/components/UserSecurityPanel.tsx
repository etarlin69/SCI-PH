import { useState, type ReactNode } from 'react';
import { AlertTriangle, KeyRound, Mail, ShieldCheck, ShieldOff } from 'lucide-react';
import type { User } from '../../../types';
import { errorMessage, usersService } from '../../../services';
import { Button, Card, ConfirmDialog, ErrorState, LoadingSpinner, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { cn } from '../../../utils/cn';
import { formatDMY } from '../../../utils/dates';
import { daysSince, formatDateTimeShort, plural } from '../format';

export interface UserSecurityPanelProps {
  user: Pick<User, 'id' | 'nombre' | 'email'>;
  canUpdate: boolean;
  onMfaReset?: () => void;
}

type Tone = 'success' | 'warning' | 'danger' | 'neutral';

const TONE: Record<Tone, string> = {
  success: 'bg-success-50 text-success-600',
  warning: 'bg-warning-50 text-warning-700',
  danger: 'bg-danger-50 text-danger-600',
  neutral: 'bg-surface-hover text-ink-3',
};

function SecurityRow({
  icon,
  tone,
  title,
  body,
  action,
}: {
  icon: ReactNode;
  tone: Tone;
  title: string;
  body: ReactNode;
  action?: ReactNode;
}) {
  return (
    <li className="flex flex-col gap-3 border-b border-line-soft py-4 first:pt-0 last:border-b-0 last:pb-0 sm:flex-row sm:items-center">
      <div className="flex min-w-0 flex-1 items-start gap-3">
        <span className={cn('flex h-9 w-9 shrink-0 items-center justify-center rounded-full', TONE[tone])}>{icon}</span>
        <div className="flex min-w-0 flex-col gap-0.5">
          <span className="text-sm font-semibold text-ink">{title}</span>
          <span className="text-[13px] text-ink-muted">{body}</span>
        </div>
      </div>
      {action && <div className="shrink-0 pl-12 sm:pl-0">{action}</div>}
    </li>
  );
}

/** Pestaña Seguridad: estado del MFA, contraseña (fechas, nunca su valor), restablecimiento pendiente e intentos fallidos de los últimos 7 días. */
export function UserSecurityPanel({ user, canUpdate, onMfaReset }: UserSecurityPanelProps) {
  const toast = useToast();
  const { data, loading, error, reload, setData } = useAsync(() => usersService.getSecurity(user.id), [user.id]);
  const [confirm, setConfirm] = useState<'reset' | 'mfa' | null>(null);

  const sendReset = async () => {
    try {
      await usersService.sendPasswordReset(user.id);
      setData((d) => (d ? { ...d, restablecimientoPendiente: true } : d));
      setConfirm(null);
      toast({ title: 'Enlace enviado', description: `Restablecimiento enviado a ${user.email}. Caduca en 30 minutos.`, tone: 'success' });
    } catch (e) {
      toast({ title: 'No se pudo enviar el enlace', description: errorMessage(e), tone: 'error' });
    }
  };

  const resetMfa = async () => {
    try {
      await usersService.resetMfa(user.id);
      setData((d) => (d ? { ...d, mfaActivo: false, codigosRecuperacion: 0, mfaDesde: undefined } : d));
      setConfirm(null);
      onMfaReset?.();
      toast({ title: 'MFA restablecido', description: 'Deberá configurar un nuevo autenticador en su próximo acceso.', tone: 'success' });
    } catch (e) {
      toast({ title: 'No se pudo restablecer el MFA', description: errorMessage(e), tone: 'error' });
    }
  };

  return (
    <Card title="Seguridad" subtitle="Las contraseñas nunca se muestran: solo puedes enviar un enlace de restablecimiento.">
      {loading && !data ? (
        <div className="flex justify-center py-10">
          <LoadingSpinner label="Cargando datos de seguridad…" />
        </div>
      ) : error ? (
        <ErrorState title="No se pudieron cargar los datos de seguridad" message={error} onRetry={reload} />
      ) : data ? (
        <ul className="flex flex-col">
          <SecurityRow
            icon={data.mfaActivo ? <ShieldCheck size={18} aria-hidden="true" /> : <ShieldOff size={18} aria-hidden="true" />}
            tone={data.mfaActivo ? 'success' : 'warning'}
            title={data.mfaActivo ? 'MFA activado · app de autenticación' : 'MFA pendiente de configurar'}
            body={
              data.mfaActivo
                ? `${data.mfaDesde ? `Configurado el ${formatDMY(data.mfaDesde)} · ` : ''}${plural(
                    data.codigosRecuperacion,
                    'código de recuperación sin usar',
                    'códigos de recuperación sin usar',
                  )}`
                : 'Se le pedirá en su próximo inicio de sesión.'
            }
            action={
              canUpdate && data.mfaActivo ? (
                <Button variant="secondary" size="sm" onClick={() => setConfirm('mfa')}>
                  Restablecer MFA
                </Button>
              ) : undefined
            }
          />
          <SecurityRow
            icon={<KeyRound size={18} aria-hidden="true" />}
            tone={data.restablecimientoPendiente ? 'warning' : data.passwordCaducaEnDias <= 7 ? 'warning' : 'neutral'}
            title="Contraseña"
            body={
              <>
                Cambiada hace {plural(daysSince(data.passwordCambiadaEl), 'día')} ({formatDMY(data.passwordCambiadaEl)}) ·{' '}
                {data.passwordCaducaEnDias > 0 ? `caduca en ${plural(data.passwordCaducaEnDias, 'día')}` : 'caducada'}
                {data.restablecimientoPendiente && (
                  <span className="mt-1 flex items-center gap-1 font-medium text-warning-700">
                    <Mail size={13} aria-hidden="true" />
                    Restablecimiento pendiente · enlace enviado
                  </span>
                )}
              </>
            }
            action={
              canUpdate ? (
                <Button variant="secondary" size="sm" leftIcon={<Mail size={14} aria-hidden="true" />} onClick={() => setConfirm('reset')}>
                  {data.restablecimientoPendiente ? 'Reenviar enlace' : 'Enviar enlace de restablecimiento'}
                </Button>
              ) : undefined
            }
          />
          <SecurityRow
            icon={<AlertTriangle size={18} aria-hidden="true" />}
            tone={data.intentosFallidos7d > 0 ? 'danger' : 'neutral'}
            title="Intentos fallidos (7 días)"
            body={
              data.intentosFallidos7d > 0
                ? `${plural(data.intentosFallidos7d, 'intento')}${
                    data.ultimoIntentoFallido
                      ? ` · último ${formatDateTimeShort(data.ultimoIntentoFallido.fecha)} desde ${data.ultimoIntentoFallido.ip}`
                      : ''
                  }`
                : 'Ningún intento fallido.'
            }
          />
        </ul>
      ) : null}

      <ConfirmDialog
        open={confirm === 'reset'}
        tone="primary"
        title="¿Restablecer la contraseña?"
        message={`Se enviará un enlace de un solo uso a ${user.email}. La contraseña actual dejará de funcionar cuando la cambie.`}
        confirmLabel="Enviar enlace"
        onConfirm={sendReset}
        onCancel={() => setConfirm(null)}
      />
      <ConfirmDialog
        open={confirm === 'mfa'}
        title="¿Restablecer MFA?"
        message="Se desvinculará su autenticador actual y los códigos de recuperación. Deberá configurarlo de nuevo en su próximo acceso."
        confirmLabel="Restablecer MFA"
        onConfirm={resetMfa}
        onCancel={() => setConfirm(null)}
      />
    </Card>
  );
}
