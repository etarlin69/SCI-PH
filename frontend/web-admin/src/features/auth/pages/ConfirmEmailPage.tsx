import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { MailCheck } from 'lucide-react';
import { describeApiError, isApiError } from '../../../api';
import { authApi } from '../../../auth';
import { Button, Input, LoadingSpinner } from '../../../components/shared';
import { useDocumentTitle } from '../../../hooks/useDocumentTitle';
import { AuthAlert } from '../components/AuthAlert';
import { AuthLayout } from '../components/AuthLayout';
import { LinkStatus } from '../components/LinkStatus';
import { EMAIL_PATTERN } from '../password';

type View = 'confirming' | 'confirmed' | 'invalid' | 'error';

/**
 * Confirmación de email desde el enlace del correo. El backend genera `/confirm-email?userId=…&token=…`
 * y POST /confirm-email exige ambos.
 */
export function ConfirmEmailPage() {
  useDocumentTitle('Confirmar email');
  const [params] = useSearchParams();
  const userId = params.get('userId') ?? '';
  const token = params.get('token') ?? '';
  const [view, setView] = useState<View>(userId && token ? 'confirming' : 'invalid');
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const started = useRef<number | null>(null);

  useEffect(() => {
    // Un solo POST por intento aunque StrictMode repita el efecto.
    if (!userId || !token || started.current === attempt) return;
    started.current = attempt;
    setView('confirming');
    authApi.confirmEmail({ userId, token }).then(
      () => setView('confirmed'),
      (e: unknown) => {
        if (isApiError(e, 400)) setView('invalid');
        else {
          setError(describeApiError(e));
          setView('error');
        }
      },
    );
  }, [userId, token, attempt]);

  let content;
  if (view === 'confirming') {
    content = (
      <div className="flex justify-center py-10">
        <LoadingSpinner label="Confirmando tu email…" />
      </div>
    );
  } else if (view === 'confirmed') {
    content = (
      <LinkStatus tone="success" title="Email confirmado" action={{ to: '/login', label: 'Ir a iniciar sesión' }}>
        Ya puedes recibir avisos y enlaces de recuperación en esta dirección.
      </LinkStatus>
    );
  } else if (view === 'error') {
    content = (
      <AuthAlert
        tone="error"
        title="No pudimos confirmar el email"
        action={
          <Button variant="secondary" size="sm" onClick={() => setAttempt((n) => n + 1)}>
            Reintentar
          </Button>
        }
      >
        {error}
      </AuthAlert>
    );
  } else {
    content = <ResendConfirmation />;
  }

  return <AuthLayout>{content}</AuthLayout>;
}

/** Enlace inválido o caducado: pedir otro. Respuesta genérica, no revela si el email existe. */
function ResendConfirmation() {
  const [email, setEmail] = useState('');
  const [touched, setTouched] = useState(false);
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const emailInvalid = !EMAIL_PATTERN.test(email.trim());

  const onResend = async (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (emailInvalid || sending) return;
    setSending(true);
    setError(null);
    try {
      await authApi.resendConfirmation(email.trim());
      setSent(true);
    } catch (err) {
      setError(describeApiError(err));
    } finally {
      setSending(false);
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <LinkStatus tone="invalid" title="El enlace no es válido o caducó">
        Escribe tu email y, si existe una cuenta pendiente de confirmar, te enviaremos un enlace nuevo.
      </LinkStatus>
      {sent ? (
        <AuthAlert tone="success" title="Solicitud recibida">
          Si existe una cuenta con ese email, recibirás un nuevo enlace en unos minutos.
        </AuthAlert>
      ) : (
        <form noValidate onSubmit={onResend} className="flex flex-col gap-4">
          {error && (
            <AuthAlert tone="error" title="No se pudo enviar la solicitud">
              {error}
            </AuthAlert>
          )}
          <Input
            label="Email"
            type="email"
            autoComplete="email"
            value={email}
            maxLength={256}
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

export default ConfirmEmailPage;
