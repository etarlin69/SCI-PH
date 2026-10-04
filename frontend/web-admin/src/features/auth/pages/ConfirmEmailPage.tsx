import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { CheckCircle2, Clock, MailCheck } from 'lucide-react';
import { Button, Input, LoadingSpinner } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDocumentTitle } from '../../../hooks/useDocumentTitle';
import { isServiceError } from '../../../services';
import { authService } from '../services/authService';
import { AuthAlert } from '../components/AuthAlert';
import { AuthLayout } from '../components/AuthLayout';
import { DemoCredentialsHint } from '../components/DemoCredentialsHint';
import { EMAIL_PATTERN } from '../password';

const PRIMARY_LINK =
  'inline-flex h-11 items-center justify-center rounded-lg bg-primary-600 px-4 text-sm font-semibold text-white hover:bg-primary-700';

/** Confirmación de email desde el enlace del correo (`/confirm-email?token=…`). */
export function ConfirmEmailPage() {
  useDocumentTitle('Confirmar email');
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';
  const result = useAsync(() => authService.confirmEmail(token), [token]);

  const [email, setEmail] = useState('');
  const [touched, setTouched] = useState(false);
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);
  const [resendError, setResendError] = useState<string | null>(null);
  const emailInvalid = !EMAIL_PATTERN.test(email.trim());

  const onResend = async (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (emailInvalid) return;
    setSending(true);
    setResendError(null);
    try {
      await authService.resendEmailConfirmation(email);
      setSent(true);
    } catch (err) {
      setResendError(isServiceError(err, 'RATE_LIMITED') ? err.message : 'No pudimos enviar el correo. Inténtalo de nuevo.');
    } finally {
      setSending(false);
    }
  };

  let content;
  if (result.loading) {
    content = (
      <div className="flex justify-center py-10">
        <LoadingSpinner label="Confirmando tu email…" />
      </div>
    );
  } else if (result.error) {
    content = (
      <AuthAlert
        tone="error"
        title="No pudimos confirmar el email"
        action={
          <Button variant="secondary" size="sm" onClick={result.reload}>
            Reintentar
          </Button>
        }
      >
        {result.error}
      </AuthAlert>
    );
  } else if (result.data === 'confirmed' || result.data === 'used') {
    content = (
      <div className="flex flex-col gap-4" role="status">
        <span className="flex h-11 w-11 items-center justify-center rounded-full bg-success-50 text-success-600">
          <CheckCircle2 size={20} aria-hidden="true" />
        </span>
        <h1 className="text-2xl font-semibold text-ink">
          {result.data === 'confirmed' ? 'Email confirmado' : 'Tu email ya estaba confirmado'}
        </h1>
        <p className="text-sm text-ink-3">Ya puedes recibir avisos y enlaces de recuperación en esta dirección.</p>
        <Link to="/login" className={PRIMARY_LINK}>
          Ir a iniciar sesión
        </Link>
      </div>
    );
  } else {
    content = (
      <div className="flex flex-col gap-5">
        <div className="flex flex-col gap-1.5">
          <span className="mb-1 flex h-11 w-11 items-center justify-center rounded-full bg-warning-50 text-warning-700">
            <Clock size={20} aria-hidden="true" />
          </span>
          <h1 className="text-2xl font-semibold text-ink">El enlace caducó</h1>
          <p className="text-sm text-ink-muted">
            Los enlaces de confirmación son válidos 24 horas. Escribe tu email y te enviaremos uno nuevo.
          </p>
        </div>
        {sent ? (
          <AuthAlert tone="success" title="Correo enviado">
            Si existe una cuenta con ese email, recibirás un nuevo enlace en unos minutos.
          </AuthAlert>
        ) : (
          <form noValidate onSubmit={onResend} className="flex flex-col gap-4">
            {resendError && (
              <AuthAlert tone="error" title="No se envió el correo">
                {resendError}
              </AuthAlert>
            )}
            <Input
              label="Email"
              type="email"
              autoComplete="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              onBlur={() => setTouched(true)}
              error={touched && emailInvalid ? 'Introduce un email válido.' : undefined}
              leftIcon={<MailCheck size={16} aria-hidden="true" />}
            />
            <Button type="submit" className="h-12 w-full sm:h-11" loading={sending} loadingText="Enviando…">
              Reenviar enlace
            </Button>
          </form>
        )}
        <Link to="/login" className="text-center text-[13px] font-semibold text-primary-600 hover:underline">
          Volver a iniciar sesión
        </Link>
      </div>
    );
  }

  return <AuthLayout footer={<DemoCredentialsHint variant="confirm" />}>{content}</AuthLayout>;
}

export default ConfirmEmailPage;
