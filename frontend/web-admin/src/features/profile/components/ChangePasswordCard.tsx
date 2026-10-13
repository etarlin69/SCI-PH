import { useState, type FormEvent } from 'react';
import { describeApiError, isApiError } from '../../../api';
import { useAuth } from '../../../auth';
import { Button, Card, FormError } from '../../../components/shared';
import { PasswordInput } from '../../auth/components/PasswordInput';
import { PasswordStrengthMeter } from '../../auth/components/PasswordStrengthMeter';
import { meetsPasswordPolicy } from '../../auth/password';

/**
 * Cambio de contraseña propio (POST /api/web/auth/change-password). Al aceptarlo el backend borra la cookie de
 * refresh y revoca las sesiones: la SPA cierra la sesión local y lleva al login con un aviso.
 */
export function ChangePasswordCard() {
  const { changePassword } = useAuth();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const mismatch = !!confirm && next !== confirm;
  const ready = !!current && meetsPasswordPolicy(next) && !!confirm && !mismatch;

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!ready || submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      await changePassword(current, next);
      // La sesión ya terminó: RequireAuth redirige al login con el aviso «Contraseña actualizada».
    } catch (err) {
      if (isApiError(err, 400)) {
        const fields = err.fieldErrors;
        setError(
          fields.currentPassword
            ? 'La contraseña actual no es correcta.'
            : fields.newPassword
              ? 'La nueva contraseña no cumple la política de seguridad.'
              : describeApiError(err),
        );
      } else {
        setError(describeApiError(err));
      }
      setSubmitting(false);
    }
  };

  return (
    <Card title="Contraseña" subtitle="Al cambiarla se cerrarán todas tus sesiones, también esta.">
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        <FormError message={error} />
        <PasswordInput label="Contraseña actual" autoComplete="current-password" value={current} maxLength={256} onChange={(e) => setCurrent(e.target.value)} />
        <div className="flex flex-col gap-3">
          <PasswordInput
            label="Nueva contraseña"
            autoComplete="new-password"
            value={next}
            maxLength={256}
            onChange={(e) => setNext(e.target.value)}
            aria-describedby="profile-pw-strength"
          />
          <PasswordStrengthMeter id="profile-pw-strength" password={next} />
        </div>
        <PasswordInput
          label="Confirmar nueva contraseña"
          autoComplete="new-password"
          value={confirm}
          maxLength={256}
          onChange={(e) => setConfirm(e.target.value)}
          error={mismatch ? 'Las contraseñas no coinciden.' : undefined}
        />
        <div className="flex justify-end">
          <Button type="submit" disabled={!ready} loading={submitting} loadingText="Actualizando…">
            Cambiar contraseña
          </Button>
        </div>
      </form>
    </Card>
  );
}
