import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { Mail } from 'lucide-react';
import { describeApiError, isApiError } from '../../../api';
import { postLoginRoute, useAuth, type SignOutReason } from '../../../auth';
import { Button, Input, LoadingSpinner } from '../../../components/shared';
import { useDocumentTitle } from '../../../hooks/useDocumentTitle';
import { AuthAlert } from '../components/AuthAlert';
import { AuthLayout } from '../components/AuthLayout';
import { PasswordInput } from '../components/PasswordInput';

const schema = z.object({
  identifier: z.string().trim().min(1, 'Escribe tu email o usuario.').max(256, 'Máximo 256 caracteres.'),
  password: z.string().min(1, 'Escribe tu contraseña.').max(256, 'Máximo 256 caracteres.'),
});
type FormValues = z.infer<typeof schema>;

export interface LoginLocationState {
  from?: string;
  reason?: 'expired';
}

const NOTICE: Partial<Record<SignOutReason | 'expired', { title: string; text: string }>> = {
  expired: { title: 'Tu sesión ha caducado', text: 'Vuelve a iniciar sesión para continuar.' },
  'password-changed': { title: 'Contraseña actualizada', text: 'Por seguridad se cerraron tus sesiones. Inicia sesión con la nueva contraseña.' },
  'mfa-changed': { title: 'Verificación en dos pasos actualizada', text: 'Por seguridad se cerraron tus sesiones. Vuelve a iniciar sesión.' },
};

/** Paso 1 del acceso: credenciales. Con MFA activo continúa en /mfa; sin MFA la sesión empieza aquí. */
export function LoginPage() {
  useDocumentTitle('Iniciar sesión');
  const { status, permissions, signOutReason, login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const state = (location.state ?? {}) as LoginLocationState;
  const [error, setError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { identifier: '', password: '' } });

  if (status === 'initializing') {
    return (
      <AuthLayout>
        <div className="flex justify-center py-10">
          <LoadingSpinner label="Comprobando la sesión…" />
        </div>
      </AuthLayout>
    );
  }
  if (status === 'authenticated') return <Navigate to={postLoginRoute(permissions, state.from)} replace />;

  const notice = NOTICE[state.reason ?? signOutReason ?? 'logout'];

  const onSubmit = async (values: FormValues) => {
    setError(null);
    try {
      const result = await login(values.identifier, values.password);
      if (result.mfaRequired) navigate('/mfa', { state: { from: state.from } });
      else navigate(postLoginRoute(result.session.user.permissions, state.from), { replace: true });
    } catch (e) {
      // 401 genérico a propósito: el backend no distingue usuario inexistente, contraseña errónea, cuenta
      // bloqueada o desactivada. La UI tampoco.
      setError(
        isApiError(e, 401)
          ? 'Email, usuario o contraseña incorrectos, o la cuenta no puede iniciar sesión en este momento.'
          : describeApiError(e),
      );
    }
  };

  return (
    <AuthLayout>
      <form noValidate onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-5">
        <div className="flex flex-col gap-1.5">
          <h1 className="text-2xl font-semibold text-ink">Iniciar sesión</h1>
          <p className="text-sm text-ink-muted">Usa tu cuenta institucional de SCI-PH.</p>
        </div>

        {notice && !error && (
          <AuthAlert tone="info" title={notice.title}>
            {notice.text}
          </AuthAlert>
        )}
        {error && (
          <AuthAlert
            tone="error"
            title="No se pudo iniciar sesión"
            action={
              <Link to="/forgot-password" className="font-semibold text-danger-text underline">
                ¿Olvidaste tu contraseña?
              </Link>
            }
          >
            {error}
          </AuthAlert>
        )}

        <Input
          label="Email o usuario"
          autoComplete="username"
          autoFocus
          leftIcon={<Mail size={16} />}
          error={errors.identifier?.message}
          className="h-12 text-base sm:h-10 sm:text-sm"
          {...register('identifier')}
        />
        <PasswordInput label="Contraseña" autoComplete="current-password" error={errors.password?.message} {...register('password')} />

        <div className="flex justify-end">
          <Link to="/forgot-password" className="text-[13px] font-semibold text-primary-600 hover:underline">
            ¿Olvidaste tu contraseña?
          </Link>
        </div>

        <Button type="submit" className="h-12 w-full sm:h-11" loading={isSubmitting} loadingText="Verificando…">
          Iniciar sesión
        </Button>
      </form>
    </AuthLayout>
  );
}

export default LoginPage;
