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
 * Activación de una cuenta invitada (`/activate-account?token=…`). El usuario solo fija su contraseña:
 * la persona (Patient/ClinicalStaff), el email y el rol los decide la invitación guardada en el backend.
 */
export function ActivateAccountPage() {
  useDocumentTitle('Activar cuenta');
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';
  const [view, setView] = useState<View>(token ? 'form' : 'invalid');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (password: string) => {
    setSubmitting(true);
    setError(null);
    try {
      await authApi.activateAccount({ token, password });
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
        <LinkStatus tone="invalid" title="El enlace no es válido o caducó">
          Las invitaciones caducan y solo se pueden usar una vez. Pide a quien te invitó que te envíe una nueva.
        </LinkStatus>
      )}
      {view === 'done' && (
        <LinkStatus tone="success" title="Cuenta activada" action={{ to: '/login', label: 'Ir a iniciar sesión' }}>
          Ya puedes iniciar sesión con tu email y la contraseña que acabas de crear.
        </LinkStatus>
      )}
      {view === 'form' && (
        <NewPasswordForm
          title="Activa tu cuenta"
          intro="Crea la contraseña con la que accederás a SCI-PH."
          submitLabel="Activar cuenta"
          submitting={submitting}
          error={error}
          onSubmit={(pw) => void submit(pw)}
        />
      )}
    </AuthLayout>
  );
}

export default ActivateAccountPage;
