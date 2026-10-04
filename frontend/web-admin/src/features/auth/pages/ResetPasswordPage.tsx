import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { CheckCircle2, Clock } from 'lucide-react';
import { Button, LoadingSpinner } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDocumentTitle } from '../../../hooks/useDocumentTitle';
import { authService, isServiceError } from '../../../services';
import { AuthAlert } from '../components/AuthAlert';
import { AuthLayout } from '../components/AuthLayout';
import { DemoCredentialsHint } from '../components/DemoCredentialsHint';
import { PasswordInput } from '../components/PasswordInput';
import { PasswordStrengthMeter } from '../components/PasswordStrengthMeter';
import { meetsPasswordPolicy } from '../password';

/** Nueva contraseña desde el enlace del correo (`/reset-password?token=…`). */
export function ResetPasswordPage() {
  useDocumentTitle('Restablecer contraseña');
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';
  const status = useAsync(() => authService.validateResetToken(token), [token]);

  const [pw, setPw] = useState('');
  const [pw2, setPw2] = useState('');
  const [touched2, setTouched2] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [expired, setExpired] = useState(false);

  const mismatch = !!pw2 && pw !== pw2;
  const ready = meetsPasswordPolicy(pw) && !!pw2 && !mismatch;

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!ready) return;
    setSubmitting(true);
    setError(null);
    try {
      await authService.resetPassword(token, pw);
      setDone(true);
    } catch (err) {
      if (isServiceError(err, 'TOKEN_EXPIRED')) setExpired(true);
      else if (isServiceError(err, 'PASSWORD_REUSED')) setError('Debe ser distinta de tus últimas 5 contraseñas.');
      else setError('No pudimos guardar la contraseña. Inténtalo de nuevo.');
    } finally {
      setSubmitting(false);
    }
  };

  let content;
  if (status.loading) {
    content = (
      <div className="flex justify-center py-10">
        <LoadingSpinner label="Comprobando el enlace…" />
      </div>
    );
  } else if (status.error) {
    content = (
      <AuthAlert
        tone="error"
        title="No pudimos comprobar el enlace"
        action={
          <Button variant="secondary" size="sm" onClick={status.reload}>
            Reintentar
          </Button>
        }
      >
        {status.error}
      </AuthAlert>
    );
  } else if (expired || status.data !== 'valid') {
    content = (
      <div className="flex flex-col gap-4">
        <span className="flex h-11 w-11 items-center justify-center rounded-full bg-warning-50 text-warning-700">
          <Clock size={20} aria-hidden="true" />
        </span>
        <h1 className="text-2xl font-semibold text-ink">
          {status.data === 'used' && !expired ? 'Este enlace ya se usó' : 'El enlace caducó'}
        </h1>
        <p className="text-sm text-ink-3">
          Los enlaces de restablecimiento son válidos 30 minutos y de un solo uso. Solicita uno nuevo para continuar.
        </p>
        <Link
          to="/forgot-password"
          className="inline-flex h-11 items-center justify-center rounded-lg bg-primary-600 px-4 text-sm font-semibold text-white hover:bg-primary-700"
        >
          Solicitar nuevo enlace
        </Link>
      </div>
    );
  } else if (done) {
    content = (
      <div className="flex flex-col gap-4" role="status">
        <span className="flex h-11 w-11 items-center justify-center rounded-full bg-success-50 text-success-600">
          <CheckCircle2 size={20} aria-hidden="true" />
        </span>
        <h1 className="text-2xl font-semibold text-ink">Contraseña actualizada</h1>
        <p className="text-sm text-ink-3">Cerramos las sesiones abiertas en otros equipos. Inicia sesión con tu nueva contraseña.</p>
        <Link
          to="/login"
          className="inline-flex h-11 items-center justify-center rounded-lg bg-primary-600 px-4 text-sm font-semibold text-white hover:bg-primary-700"
        >
          Ir a iniciar sesión
        </Link>
      </div>
    );
  } else {
    content = (
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        <div className="flex flex-col gap-1.5">
          <h1 className="text-2xl font-semibold text-ink">Crea una nueva contraseña</h1>
          <p className="text-sm text-ink-muted">Debe ser distinta de tus últimas 5 contraseñas.</p>
        </div>
        {error && (
          <AuthAlert tone="error" title="No se guardó la contraseña">
            {error}
          </AuthAlert>
        )}
        <div className="flex flex-col gap-3">
          <PasswordInput
            label="Nueva contraseña"
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
        <Button type="submit" className="h-12 w-full sm:h-11" disabled={!ready} loading={submitting} loadingText="Guardando…">
          Guardar contraseña
        </Button>
      </form>
    );
  }

  return <AuthLayout footer={<DemoCredentialsHint variant="reset" />}>{content}</AuthLayout>;
}

export default ResetPasswordPage;
