import { useState, type FormEvent } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { ArrowLeft, KeyRound, Smartphone } from 'lucide-react';
import { describeApiError, isApiError } from '../../../api';
import { postLoginRoute, useAuth } from '../../../auth';
import { Button, Input } from '../../../components/shared';
import { useDocumentTitle } from '../../../hooks/useDocumentTitle';
import { AuthAlert } from '../components/AuthAlert';
import { AuthLayout } from '../components/AuthLayout';
import { OtpInput } from '../components/OtpInput';

type Method = 'totp' | 'recovery';

/**
 * Códigos de recuperación de ASP.NET Identity: 10 caracteres alfanuméricos con guion central (`xxxxx-xxxxx`).
 * Se acepta con o sin guion y en cualquier caso; el backend es quien valida. Solo se filtran entradas imposibles.
 */
export const RECOVERY_CODE_PATTERN = /^[A-Za-z0-9]{5}-?[A-Za-z0-9]{5}$/;

export const isChallengeExpired = (expiresAtUtc: string, now = Date.now()) => Date.parse(expiresAtUtc) <= now;

/**
 * Paso 2 del acceso: TOTP o código de recuperación. El desafío es de corta duración y solo produce una sesión;
 * un código erróneo no lo consume, pero cada fallo cuenta para el bloqueo de la cuenta.
 */
export function MfaPage() {
  useDocumentTitle('Verificación en dos pasos');
  const { status, permissions, mfaChallenge, completeMfa, cancelMfa } = useAuth();
  const navigate = useNavigate();
  const from = (useLocation().state as { from?: string } | null)?.from;

  const [method, setMethod] = useState<Method>('totp');
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [challengeEnded, setChallengeEnded] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  if (status === 'authenticated') return <Navigate to={postLoginRoute(permissions, from)} replace />;
  if (!mfaChallenge && !challengeEnded) return <Navigate to="/login" replace />;

  const validate = (value: string): string | null => {
    if (method === 'totp') return /^\d{6}$/.test(value) ? null : 'Escribe los 6 dígitos del código.';
    return RECOVERY_CODE_PATTERN.test(value.trim()) ? null : 'Revisa el código de recuperación (10 caracteres).';
  };

  const submit = async (value = code) => {
    if (submitting) return;
    if (mfaChallenge && isChallengeExpired(mfaChallenge.expiresAtUtc)) {
      cancelMfa();
      setChallengeEnded(true);
      return;
    }
    const invalid = validate(value);
    if (invalid) {
      setError(invalid);
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const session = await completeMfa(method === 'totp' ? { code: value } : { recoveryCode: value.trim() });
      navigate(postLoginRoute(session.user.permissions, from), { replace: true });
    } catch (e) {
      if (isApiError(e, 401)) {
        // Mismo 401 genérico para código erróneo, desafío caducado/consumido o cuenta bloqueada.
        if (mfaChallenge && isChallengeExpired(mfaChallenge.expiresAtUtc)) {
          cancelMfa();
          setChallengeEnded(true);
        } else {
          setCode('');
          setError(
            'No se pudo verificar el código. Revisa que sea el actual e inténtalo de nuevo; si sigue fallando, vuelve a iniciar sesión.',
          );
        }
      } else {
        setError(describeApiError(e));
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
    cancelMfa();
    navigate('/login', { replace: true, state: { from } });
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

  if (challengeEnded) {
    return (
      <AuthLayout>
        <div className="flex flex-col gap-5">
          {backLink}
          <AuthAlert tone="warning" title="La verificación caducó">
            Por seguridad la verificación en dos pasos tiene un tiempo limitado. Vuelve a iniciar sesión para recibir una nueva.
          </AuthAlert>
          <Button onClick={backToLogin}>Volver al inicio de sesión</Button>
        </div>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout>
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
              : 'Usa uno de los códigos de recuperación que guardaste al activar la verificación. Cada código sirve una sola vez.'}
          </p>
        </div>

        {method === 'totp' ? (
          <OtpInput label="Código de 6 dígitos" value={code} onChange={setCode} invalid={!!error} disabled={submitting} />
        ) : (
          <Input
            label="Código de recuperación"
            autoComplete="one-time-code"
            autoCapitalize="off"
            spellCheck={false}
            value={code}
            maxLength={16}
            onChange={(e) => setCode(e.target.value)}
            className="h-12 font-mono text-base tracking-widest"
          />
        )}

        <div aria-live="assertive">
          {error && (
            <p role="alert" className="text-[13px] font-medium text-danger-text">
              {error}
            </p>
          )}
        </div>

        <Button type="submit" className="h-12 w-full sm:h-11" loading={submitting} loadingText="Verificando…">
          Verificar
        </Button>
        <Button
          type="button"
          variant="ghost"
          disabled={submitting}
          onClick={() => {
            setMethod((m) => (m === 'totp' ? 'recovery' : 'totp'));
            setCode('');
            setError(null);
          }}
        >
          {method === 'totp' ? 'Usar un código de recuperación' : 'Usar el código de la app'}
        </Button>
        <p className="text-center text-xs text-ink-muted">
          ¿Perdiste el acceso a tu app?{' '}
          <Link to="/forgot-password" className="font-semibold text-primary-600 hover:underline">
            Recuperar acceso
          </Link>
        </p>
      </form>
    </AuthLayout>
  );
}

export default MfaPage;
