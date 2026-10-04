import { useEffect, useState, type FormEvent } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { ArrowLeft, KeyRound, Smartphone } from 'lucide-react';
import { useAuth } from '../../../auth';
import { NAV_SECTIONS } from '../../../components/common/navigation';
import { Button, Checkbox, Input } from '../../../components/shared';
import { useDocumentTitle } from '../../../hooks/useDocumentTitle';
import { isServiceError } from '../../../services';
import type { MfaMethod, SessionUser } from '../../../types';
import { AuthAlert } from '../components/AuthAlert';
import { AuthLayout } from '../components/AuthLayout';
import { DemoCredentialsHint } from '../components/DemoCredentialsHint';
import { OtpInput } from '../components/OtpInput';

const RECOVERY_PATTERN = /^[A-Z0-9]{4}-[A-Z0-9]{4}$/;
const TOTP_PERIOD = 30;

function firstAllowedRoute(user: SessionUser, from?: string): string {
  if (from && from !== '/' && from !== '/admin') return from;
  const items = NAV_SECTIONS.flatMap((s) => s.items);
  return items.find((i) => i.module && user.permisos[i.module]?.includes('read'))?.to ?? '/403';
}

function useTotpCountdown(): number {
  const [s, setS] = useState(() => TOTP_PERIOD - (Math.floor(Date.now() / 1000) % TOTP_PERIOD));
  useEffect(() => {
    const t = window.setInterval(() => setS(TOTP_PERIOD - (Math.floor(Date.now() / 1000) % TOTP_PERIOD)), 1000);
    return () => window.clearInterval(t);
  }, []);
  return s;
}

/** Paso 2 del acceso: código TOTP o código de recuperación. */
export function MfaPage() {
  useDocumentTitle('Verificación en dos pasos');
  const { user, challenge, verifyMfa, cancelChallenge } = useAuth();
  const navigate = useNavigate();
  const from = (useLocation().state as { from?: string } | null)?.from;
  const period = useTotpCountdown();

  const [method, setMethod] = useState<MfaMethod>('totp');
  const [code, setCode] = useState('');
  const [trust, setTrust] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [attemptsLeft, setAttemptsLeft] = useState<number | null>(null);
  const [locked, setLocked] = useState(false);
  const [expired, setExpired] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  if (user && !submitting) return <Navigate to={firstAllowedRoute(user, from)} replace />;
  if (!user && !challenge && !locked && !expired) return <Navigate to="/login" replace />;

  const validate = (value: string): string | null => {
    if (method === 'totp') return /^\d{6}$/.test(value) ? null : 'Escribe los 6 dígitos del código.';
    return RECOVERY_PATTERN.test(value.toUpperCase()) ? null : 'Formato esperado: XXXX-XXXX.';
  };

  const submit = async (value = code) => {
    const invalid = validate(value);
    if (invalid) {
      setError(invalid);
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const session = await verifyMfa(value.toUpperCase(), method);
      navigate(firstAllowedRoute(session.user, from), { replace: true });
    } catch (e) {
      if (isServiceError(e, 'INVALID_CODE')) {
        setAttemptsLeft(Number(e.details.attemptsLeft ?? 0));
        setError(
          method === 'totp'
            ? 'Código incorrecto o caducado. Usa el código actual de tu app.'
            : 'Código de recuperación no válido o ya utilizado.',
        );
        setCode('');
      } else if (isServiceError(e, 'ACCOUNT_LOCKED')) {
        setLocked(true);
      } else if (isServiceError(e, 'TOKEN_EXPIRED')) {
        setExpired(true);
      } else {
        setError('No pudimos conectar con el servidor. Inténtalo de nuevo.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const onSubmit = (e: FormEvent) => {
    e.preventDefault();
    void submit();
  };

  const backToLogin = () => {
    cancelChallenge();
    navigate('/login', { replace: true });
  };

  const backLink = (
    <button
      type="button"
      onClick={backToLogin}
      className="inline-flex w-fit items-center gap-1.5 text-[13px] font-semibold text-primary-600 hover:underline"
    >
      <ArrowLeft size={14} aria-hidden="true" />
      Volver al inicio de sesión
    </button>
  );

  if (locked || expired) {
    return (
      <AuthLayout>
        <div className="flex flex-col gap-5">
          {backLink}
          {locked ? (
            <AuthAlert tone="locked" title="Verificación bloqueada">
              Superaste los 5 intentos permitidos. Por seguridad, el acceso queda bloqueado durante 15 minutos. Si perdiste el acceso a tu
              app de autenticación, pide al administrador que restablezca tu verificación en dos pasos.
            </AuthAlert>
          ) : (
            <AuthAlert tone="warning" title="La verificación caducó">
              Vuelve a iniciar sesión para recibir un nuevo reto.
            </AuthAlert>
          )}
          <Button onClick={backToLogin}>Volver al inicio de sesión</Button>
        </div>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout footer={<DemoCredentialsHint variant="mfa" />}>
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        {backLink}
        <div className="flex flex-col gap-1.5">
          <span className="flex h-11 w-11 items-center justify-center rounded-full bg-primary-100 text-primary-600">
            {method === 'totp' ? <Smartphone size={20} aria-hidden="true" /> : <KeyRound size={20} aria-hidden="true" />}
          </span>
          <h1 className="mt-2 text-2xl font-semibold text-ink">Verificación en dos pasos</h1>
          <p className="text-sm text-ink-muted">
            {method === 'totp'
              ? 'Abre tu app de autenticación e introduce el código que muestra para SCI-PH.'
              : 'Usa uno de los códigos de recuperación que guardaste al activar la verificación.'}
            {challenge && <span className="block pt-1 text-xs">Cuenta: {challenge.maskedEmail}</span>}
          </p>
        </div>

        {method === 'totp' ? (
          <div className="flex flex-col gap-2">
            <div className="flex items-baseline justify-between text-[13px]">
              <span className="font-semibold text-ink-2" id="otp-label">
                Código de 6 dígitos
              </span>
              <span className="tabular text-xs text-ink-muted">Cambia en {period} s</span>
            </div>
            <OtpInput
              label="Código de 6 dígitos"
              value={code}
              onChange={setCode}
              invalid={!!error}
              disabled={submitting}
              onComplete={(v) => void submit(v)}
            />
          </div>
        ) : (
          <Input
            label="Código de recuperación"
            placeholder="XXXX-XXXX"
            autoComplete="one-time-code"
            value={code}
            maxLength={9}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
            hint="Cada código de recuperación se puede usar una sola vez."
            className="h-12 font-mono text-base tracking-widest"
          />
        )}

        <div aria-live="assertive" className="flex flex-col gap-1">
          {error && (
            <p role="alert" className="text-[13px] font-medium text-danger-text">
              {error}
            </p>
          )}
          {attemptsLeft !== null && <p className="text-xs text-ink-muted">Intentos restantes: {attemptsLeft} de 5</p>}
        </div>

        <Checkbox label="Confiar en este equipo durante 30 días" checked={trust} onChange={(e) => setTrust(e.target.checked)} />

        <Button type="submit" className="h-12 w-full sm:h-11" loading={submitting} loadingText="Verificando…">
          Verificar
        </Button>
        <Button
          type="button"
          variant="ghost"
          onClick={() => {
            setMethod((m) => (m === 'totp' ? 'recovery' : 'totp'));
            setCode('');
            setError(null);
          }}
        >
          {method === 'totp' ? 'Usar un código de recuperación' : 'Usar el código de la app'}
        </Button>
        <p className="text-center text-xs text-ink-muted">
          ¿Problemas?{' '}
          <Link to="/forgot-password" className="font-semibold text-primary-600 hover:underline">
            Recuperar acceso
          </Link>
        </p>
      </form>
    </AuthLayout>
  );
}

export default MfaPage;
