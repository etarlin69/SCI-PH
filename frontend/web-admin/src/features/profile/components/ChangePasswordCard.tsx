import { useState, type FormEvent } from 'react';
import { Button, Card, FormError, useToast } from '../../../components/shared';
import { useSubmit } from '../../../hooks/useSubmit';
import { PasswordInput } from '../../auth/components/PasswordInput';
import { PasswordStrengthMeter } from '../../auth/components/PasswordStrengthMeter';
import { meetsPasswordPolicy } from '../../auth/password';

export interface ChangePasswordCardProps {
  onChange: (current: string, next: string) => Promise<void>;
}

/** Cambio de contraseña propio. */
export function ChangePasswordCard({ onChange }: ChangePasswordCardProps) {
  const toast = useToast();
  const submit = useSubmit();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const mismatch = !!confirm && next !== confirm;
  const ready = !!current && meetsPasswordPolicy(next) && !!confirm && !mismatch;

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!ready) return;
    const ok = await submit.run(async () => {
      await onChange(current, next);
      return true;
    });
    if (ok) {
      setCurrent('');
      setNext('');
      setConfirm('');
      toast({ title: 'Contraseña actualizada', description: 'Se cerraron tus sesiones en otros equipos.', tone: 'success' });
    }
  };

  return (
    <Card title="Contraseña" subtitle="Demo: la contraseña actual es Clave2026!">
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        <FormError message={submit.error} />
        <PasswordInput
          label="Contraseña actual"
          autoComplete="current-password"
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
        />
        <div className="flex flex-col gap-3">
          <PasswordInput
            label="Nueva contraseña"
            autoComplete="new-password"
            value={next}
            onChange={(e) => setNext(e.target.value)}
            aria-describedby="profile-pw-strength"
          />
          <PasswordStrengthMeter id="profile-pw-strength" password={next} />
        </div>
        <PasswordInput
          label="Confirmar nueva contraseña"
          autoComplete="new-password"
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          error={mismatch ? 'Las contraseñas no coinciden.' : undefined}
        />
        <div className="flex justify-end">
          <Button type="submit" disabled={!ready} loading={submit.submitting} loadingText="Actualizando…">
            Cambiar contraseña
          </Button>
        </div>
      </form>
    </Card>
  );
}
