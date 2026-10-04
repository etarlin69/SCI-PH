import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { Mail } from 'lucide-react';
import { useAuth } from '../../../auth';
import { Button, Checkbox, Input } from '../../../components/shared';
import { useDocumentTitle } from '../../../hooks/useDocumentTitle';
import { isServiceError } from '../../../services';
import { AuthAlert } from '../components/AuthAlert';
import { AuthLayout } from '../components/AuthLayout';
import { DemoCredentialsHint } from '../components/DemoCredentialsHint';
import { PasswordInput } from '../components/PasswordInput';
import { EMAIL_PATTERN } from '../password';

const schema = z.object({
  usuario: z
    .string()
    .trim()
    .min(1, 'Escribe tu email o usuario.')
    .refine((v) => !v.includes('@') || EMAIL_PATTERN.test(v), 'Formato de email inválido.'),
  password: z.string().min(1, 'Escribe tu contraseña.'),
  recordarEquipo: z.boolean(),
});
type FormValues = z.infer<typeof schema>;

type Banner =
  { kind: 'invalid'; attemptsLeft: number } | { kind: 'locked'; until: number } | { kind: 'server' } | { kind: 'expired' } | null;

export interface LoginLocationState {
  from?: string;
  reason?: 'expired';
}

function useCountdown(until: number | null): string {
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    if (!until) return;
    const t = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(t);
  }, [until]);
  if (!until) return '';
  const secs = Math.max(0, Math.ceil((until - now) / 1000));
  return `${Math.floor(secs / 60)}:${String(secs % 60).padStart(2, '0')}`;
}

/** Paso 1 del acceso: credenciales. */
export function LoginPage() {
  useDocumentTitle('Iniciar sesión');
  const { user, login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const state = (location.state ?? {}) as LoginLocationState;
  const [banner, setBanner] = useState<Banner>(state.reason === 'expired' ? { kind: 'expired' } : null);
  const lockedUntil = banner?.kind === 'locked' ? banner.until : null;
  const countdown = useCountdown(lockedUntil);
  const locked = !!lockedUntil && countdown !== '0:00';

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { usuario: '', password: '', recordarEquipo: false } });

  if (user) return <Navigate to={state.from ?? '/admin'} replace />;

  const onSubmit = async (values: FormValues) => {
    setBanner(null);
    try {
      await login(values);
      navigate('/mfa', { state: { from: state.from } });
    } catch (e) {
      if (isServiceError(e, 'INVALID_CREDENTIALS')) setBanner({ kind: 'invalid', attemptsLeft: Number(e.details.attemptsLeft ?? 0) });
      else if (isServiceError(e, 'ACCOUNT_LOCKED')) setBanner({ kind: 'locked', until: Number(e.details.lockedUntil) });
      else setBanner({ kind: 'server' });
    }
  };

  return (
    <AuthLayout footer={<DemoCredentialsHint variant="login" />}>
      <form noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-5">
        <div className="flex flex-col gap-1.5">
          <h1 className="text-2xl font-semibold text-ink">Iniciar sesión</h1>
          <p className="text-sm text-ink-muted">Usa tu cuenta institucional de SCI-PH.</p>
        </div>

        {banner?.kind === 'invalid' && (
          <AuthAlert tone="error" title="Email o contraseña incorrectos">
            Revisa tus datos. Por seguridad, la cuenta se bloquea tras 5 intentos fallidos.
            {banner.attemptsLeft > 0 && ` Te quedan ${banner.attemptsLeft} ${banner.attemptsLeft === 1 ? 'intento' : 'intentos'}.`}
          </AuthAlert>
        )}
        {banner?.kind === 'locked' && (
          <AuthAlert
            tone="locked"
            title="Cuenta bloqueada temporalmente"
            action={
              <Link to="/forgot-password" className="font-semibold text-danger-text underline">
                Restablecer contraseña
              </Link>
            }
          >
            {locked ? (
              <>
                Demasiados intentos fallidos. Podrás intentarlo de nuevo en <span className="tabular font-semibold">{countdown}</span>.
              </>
            ) : (
              'Ya puedes volver a intentarlo.'
            )}
          </AuthAlert>
        )}
        {banner?.kind === 'server' && (
          <AuthAlert tone="error" title="No pudimos conectar con el servidor">
            Comprueba tu conexión e inténtalo de nuevo.
          </AuthAlert>
        )}
        {banner?.kind === 'expired' && (
          <AuthAlert tone="info" title="Tu sesión ha caducado">
            Por seguridad cerramos la sesión tras 15 minutos de inactividad. Vuelve a iniciar sesión.
          </AuthAlert>
        )}

        <Input
          label="Email o usuario"
          autoComplete="username"
          autoFocus
          leftIcon={<Mail size={16} />}
          error={errors.usuario?.message}
          className="h-12 text-base sm:h-11 sm:text-[15px]"
          {...register('usuario')}
        />
        <PasswordInput label="Contraseña" autoComplete="current-password" error={errors.password?.message} {...register('password')} />

        <div className="flex flex-wrap items-center justify-between gap-3">
          <Checkbox label="Recordar este equipo" {...register('recordarEquipo')} />
          <Link to="/forgot-password" className="text-[13px] font-semibold text-primary-600 hover:underline">
            ¿Olvidaste tu contraseña?
          </Link>
        </div>

        <Button type="submit" className="h-12 w-full sm:h-11" loading={isSubmitting} loadingText="Verificando…" disabled={locked}>
          Iniciar sesión
        </Button>
      </form>
    </AuthLayout>
  );
}

export default LoginPage;
