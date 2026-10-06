import { useState, type FormEvent, type ReactNode } from 'react';
import { Button } from '../../../components/shared';
import { meetsPasswordPolicy } from '../password';
import { AuthAlert } from './AuthAlert';
import { PasswordInput } from './PasswordInput';
import { PasswordStrengthMeter } from './PasswordStrengthMeter';

export interface NewPasswordFormProps {
  title: string;
  intro?: ReactNode;
  submitLabel: string;
  submitting: boolean;
  error: string | null;
  onSubmit: (password: string) => void;
}

/** Nueva contraseña + confirmación con la política del backend (IdentityPolicy). El backend vuelve a validarla. */
export function NewPasswordForm({ title, intro, submitLabel, submitting, error, onSubmit }: NewPasswordFormProps) {
  const [pw, setPw] = useState('');
  const [pw2, setPw2] = useState('');
  const [touched2, setTouched2] = useState(false);

  const mismatch = !!pw2 && pw !== pw2;
  const ready = meetsPasswordPolicy(pw) && !!pw2 && !mismatch;

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (ready && !submitting) onSubmit(pw);
  };

  return (
    <form noValidate onSubmit={submit} className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <h1 className="text-2xl font-semibold text-ink">{title}</h1>
        {intro && <p className="text-sm text-ink-muted">{intro}</p>}
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
          maxLength={256}
          onChange={(e) => setPw(e.target.value)}
          aria-describedby="pw-strength"
        />
        <PasswordStrengthMeter id="pw-strength" password={pw} />
      </div>
      <PasswordInput
        label="Confirmar contraseña"
        autoComplete="new-password"
        value={pw2}
        maxLength={256}
        onChange={(e) => setPw2(e.target.value)}
        onBlur={() => setTouched2(true)}
        error={touched2 && mismatch ? 'Las contraseñas no coinciden.' : undefined}
        success={pw2 && !mismatch ? 'Coinciden' : undefined}
      />
      <Button type="submit" className="h-12 w-full sm:h-11" disabled={!ready} loading={submitting} loadingText="Guardando…">
        {submitLabel}
      </Button>
    </form>
  );
}
