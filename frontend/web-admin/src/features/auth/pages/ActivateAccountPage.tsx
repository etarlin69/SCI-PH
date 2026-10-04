import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { CheckCircle2, Clock, UserCheck } from 'lucide-react';
import { Button, LoadingSpinner } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDocumentTitle } from '../../../hooks/useDocumentTitle';
import { isServiceError } from '../../../services';
import { authService } from '../services/authService';
import { AuthAlert } from '../components/AuthAlert';
import { AuthLayout } from '../components/AuthLayout';
import { DemoCredentialsHint } from '../components/DemoCredentialsHint';
import { PasswordInput } from '../components/PasswordInput';
import { PasswordStrengthMeter } from '../components/PasswordStrengthMeter';
import { meetsPasswordPolicy } from '../password';

const PRIMARY_LINK =
  'inline-flex h-11 items-center justify-center rounded-lg bg-primary-600 px-4 text-sm font-semibold text-white hover:bg-primary-700';

/** Activación de cuenta desde el enlace de invitación (`/activate-account?token=…`): primera contraseña. */
export function ActivateAccountPage() {
  useDocumentTitle('Activar cuenta');
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';
  const info = useAsync(() => authService.validateActivationToken(token), [token]);

  const [pw, setPw] = useState('');
  const [pw2, setPw2] = useState('');
  const [touched2, setTouched2] = useState(false);
  const [accepted, setAccepted] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [expired, setExpired] = useState(false);

  const mismatch = !!pw2 && pw !== pw2;
  const ready = meetsPasswordPolicy(pw) && !!pw2 && !mismatch && accepted;

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!ready) return;
    setSubmitting(true);
    setError(null);
    try {
      await authService.activateAccount(token, pw);
      setDone(true);
    } catch (err) {
      if (isServiceError(err, 'TOKEN_EXPIRED')) setExpired(true);
      else setError('No pudimos activar la cuenta. Inténtalo de nuevo.');
    } finally {
      setSubmitting(false);
    }
  };

  let content;
  if (info.loading) {
    content = (
      <div className="flex justify-center py-10">
        <LoadingSpinner label="Comprobando la invitación…" />
      </div>
    );
  } else if (info.error) {
    content = (
      <AuthAlert
        tone="error"
        title="No pudimos comprobar el enlace"
        action={
          <Button variant="secondary" size="sm" onClick={info.reload}>
            Reintentar
          </Button>
        }
      >
        {info.error}
      </AuthAlert>
    );
  } else if (done || info.data?.status === 'used') {
    content = (
      <div className="flex flex-col gap-4" role="status">
        <span className="flex h-11 w-11 items-center justify-center rounded-full bg-success-50 text-success-600">
          <CheckCircle2 size={20} aria-hidden="true" />
        </span>
        <h1 className="text-2xl font-semibold text-ink">{done ? 'Cuenta activada' : 'Esta cuenta ya está activa'}</h1>
        <p className="text-sm text-ink-3">
          {done
            ? 'Ya puedes iniciar sesión. En el primer acceso te pediremos configurar la verificación en dos pasos.'
            : 'El enlace ya se usó. Inicia sesión con tu email y contraseña.'}
        </p>
        <Link to="/login" className={PRIMARY_LINK}>
          Ir a iniciar sesión
        </Link>
      </div>
    );
  } else if (expired || info.data?.status !== 'valid') {
    content = (
      <div className="flex flex-col gap-4">
        <span className="flex h-11 w-11 items-center justify-center rounded-full bg-warning-50 text-warning-700">
          <Clock size={20} aria-hidden="true" />
        </span>
        <h1 className="text-2xl font-semibold text-ink">La invitación caducó</h1>
        <p className="text-sm text-ink-3">
          Los enlaces de activación son válidos 7 días. Pide al administrador que te reenvíe la invitación.
        </p>
        <Link to="/login" className={PRIMARY_LINK}>
          Volver a iniciar sesión
        </Link>
      </div>
    );
  } else {
    const d = info.data;
    content = (
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        <div className="flex flex-col gap-1.5">
          <span className="mb-1 flex h-11 w-11 items-center justify-center rounded-full bg-primary-100 text-primary-600">
            <UserCheck size={20} aria-hidden="true" />
          </span>
          <h1 className="text-2xl font-semibold text-ink">Activa tu cuenta</h1>
          <p className="text-sm text-ink-muted">
            Hola <strong className="text-ink">{d.nombre}</strong>, te invitaron como <strong className="text-ink">{d.rol}</strong>. Crea tu
            contraseña para <span className="break-all">{d.email}</span>.
          </p>
        </div>
        {error && (
          <AuthAlert tone="error" title="No se activó la cuenta">
            {error}
          </AuthAlert>
        )}
        <div className="flex flex-col gap-3">
          <PasswordInput
            label="Contraseña"
            autoComplete="new-password"
            value={pw}
            onChange={(e) => setPw(e.target.value)}
            aria-describedby="pw-strength"
          />
          <PasswordStrengthMeter id="pw-strength" password={pw} />
        </div>
        <PasswordInput
          label="Confirmar contraseña"
          autoComplete="new-password"
          value={pw2}
          onChange={(e) => setPw2(e.target.value)}
          onBlur={() => setTouched2(true)}
          error={touched2 && mismatch ? 'Las contraseñas no coinciden.' : undefined}
          success={pw2 && !mismatch ? 'Coinciden' : undefined}
        />
        <label className="flex items-start gap-2.5 text-[13px] text-ink-2">
          <input
            type="checkbox"
            checked={accepted}
            onChange={(e) => setAccepted(e.target.checked)}
            className="mt-0.5 h-4 w-4 accent-primary-600"
          />
          <span>Acepto la política de uso y confidencialidad de datos clínicos del SCI-PH.</span>
        </label>
        <Button type="submit" className="h-12 w-full sm:h-11" disabled={!ready} loading={submitting} loadingText="Activando…">
          Activar cuenta
        </Button>
      </form>
    );
  }

  return <AuthLayout footer={<DemoCredentialsHint variant="activate" />}>{content}</AuthLayout>;
}

export default ActivateAccountPage;
