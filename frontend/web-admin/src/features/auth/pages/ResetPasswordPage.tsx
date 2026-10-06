import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { describeApiError, isApiError } from '../../../api';
import { authApi } from '../../../auth';
import { useDocumentTitle } from '../../../hooks/useDocumentTitle';
import { AuthLayout } from '../components/AuthLayout';
import { LinkStatus, isPasswordPolicyError } from '../components/LinkStatus';
import { NewPasswordForm } from '../components/NewPasswordForm';

type View = 'form' | 'done' | 'invalid';

/**
 * Nueva contraseña desde el enlace del correo. El backend genera `/reset-password?userId=…&token=…`
 * (AccountRecoveryService) y exige ambos. No hay GET de validación previa: el enlace se valida al enviar.
 */
export function ResetPasswordPage() {
  useDocumentTitle('Restablecer contraseña');
  const [params] = useSearchParams();
  const userId = params.get('userId') ?? '';
  const token = params.get('token') ?? '';
  const [view, setView] = useState<View>(userId && token ? 'form' : 'invalid');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (newPassword: string) => {
    setSubmitting(true);
    setError(null);
    try {
      await authApi.resetPassword({ userId, token, newPassword });
      setView('done');
    } catch (e) {
      if (isApiError(e, 400)) {
        if (isPasswordPolicyError(e.fieldErrors)) setError('La contraseña no cumple la política de seguridad.');
        else setView('invalid');
      } else {
        setError(describeApiError(e));
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <AuthLayout>
      {view === 'invalid' && (
        <LinkStatus tone="invalid" title="El enlace no es válido o caducó" action={{ to: '/forgot-password', label: 'Solicitar un enlace nuevo' }}>
          Los enlaces de restablecimiento caducan y solo se pueden usar una vez. Solicita uno nuevo para continuar.
        </LinkStatus>
      )}
      {view === 'done' && (
        <LinkStatus tone="success" title="Contraseña actualizada" action={{ to: '/login', label: 'Ir a iniciar sesión' }}>
          Por seguridad se cerraron las sesiones abiertas. Inicia sesión con tu nueva contraseña.
        </LinkStatus>
      )}
      {view === 'form' && (
        <NewPasswordForm
          title="Crea una nueva contraseña"
          submitLabel="Guardar contraseña"
          submitting={submitting}
          error={error}
          onSubmit={(pw) => void submit(pw)}
        />
      )}
    </AuthLayout>
  );
}

export default ResetPasswordPage;
