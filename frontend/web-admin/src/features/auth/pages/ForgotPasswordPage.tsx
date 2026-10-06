import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Link } from 'react-router-dom';
import { ArrowLeft, MailCheck } from 'lucide-react';
import { Button, Input } from '../../../components/shared';
import { useDocumentTitle } from '../../../hooks/useDocumentTitle';
import { describeApiError, isApiError } from '../../../api';
import { authApi } from '../../../auth';
import { AuthAlert } from '../components/AuthAlert';
import { AuthLayout } from '../components/AuthLayout';
import { EMAIL_PATTERN } from '../password';

const schema = z.object({
  email: z.string().trim().min(1, 'Escribe tu email.').regex(EMAIL_PATTERN, 'Formato de email inválido. Ejemplo: nombre@ejemplo.org'),
});
type FormValues = z.infer<typeof schema>;

/** Solicitud de enlace de restablecimiento. El backend responde 202 genérico exista o no la cuenta. */
export function ForgotPasswordPage() {
  useDocumentTitle('Recuperar contraseña');
  const [sentTo, setSentTo] = useState<string | null>(null);
  const [banner, setBanner] = useState<{ title: string; body: string } | null>(null);
  const [resending, setResending] = useState(false);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { email: '' } });

  const send = async (email: string) => {
    setBanner(null);
    try {
      await authApi.forgotPassword(email);
      setSentTo(email);
    } catch (e) {
      setBanner({
        title: isApiError(e, 429) ? 'Demasiadas solicitudes' : 'No pudimos procesar la solicitud',
        body: describeApiError(e),
      });
    }
  };

  return (
    <AuthLayout>
      <div className="flex flex-col gap-5">
        <Link to="/login" className="inline-flex w-fit items-center gap-1.5 text-[13px] font-semibold text-primary-600 hover:underline">
          <ArrowLeft size={14} aria-hidden="true" />
          Volver al inicio de sesión
        </Link>

        {sentTo ? (
          <div className="flex flex-col gap-4" role="status">
            <span className="flex h-11 w-11 items-center justify-center rounded-full bg-success-50 text-success-600">
              <MailCheck size={20} aria-hidden="true" />
            </span>
            <h1 className="text-2xl font-semibold text-ink">Revisa tu correo</h1>
            <p className="text-sm text-ink-3">
              Si existe una cuenta asociada a <strong className="break-all">{sentTo}</strong>, recibirás un enlace para restablecer la
              contraseña. El enlace caduca y solo se puede usar una vez.
            </p>
            <p className="text-[13px] text-ink-muted">¿No llega? Revisa la carpeta de spam o solicita otro enlace.</p>
            {banner && (
              <AuthAlert tone="warning" title={banner.title}>
                {banner.body}
              </AuthAlert>
            )}
            <Button
              variant="secondary"
              loading={resending}
              loadingText="Enviando…"
              onClick={async () => {
                setResending(true);
                await send(sentTo);
                setResending(false);
              }}
            >
              Reenviar enlace
            </Button>
          </div>
        ) : (
          <form noValidate onSubmit={handleSubmit((v) => send(v.email.trim()))} className="flex flex-col gap-5">
            <div className="flex flex-col gap-1.5">
              <h1 className="text-2xl font-semibold text-ink">Recuperar contraseña</h1>
              <p className="text-sm text-ink-muted">
                Escribe el email de tu cuenta y te enviaremos un enlace para crear una nueva contraseña.
              </p>
            </div>
            {banner && (
              <AuthAlert tone="error" title={banner.title}>
                {banner.body}
              </AuthAlert>
            )}
            <Input
              label="Email"
              type="email"
              autoComplete="email"
              autoFocus
              hint="Usa el email institucional con el que inicias sesión."
              error={errors.email?.message}
              className="h-12 text-base sm:h-11 sm:text-[15px]"
              {...register('email')}
            />
            <Button type="submit" className="h-12 w-full sm:h-11" loading={isSubmitting} loadingText="Enviando…">
              Enviar enlace
            </Button>
          </form>
        )}
      </div>
    </AuthLayout>
  );
}

export default ForgotPasswordPage;
