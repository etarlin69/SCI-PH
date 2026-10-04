import { useEffect, useState } from 'react';
import { Check, Copy, KeyRound, RefreshCw } from 'lucide-react';
import { Button } from '../../../components/shared';

export interface TemporaryPasswordFieldProps {
  password: string;
  onRegenerate: () => void;
  onCopied: () => void;
  copied: boolean;
  error?: string;
}

/** Contraseña temporal de 16 caracteres: **nunca se muestra en pantalla**; solo puede copiarse al portapapeles (para entregarla por un canal seguro) o regenerarse. */
export function TemporaryPasswordField({ password, onRegenerate, onCopied, copied, error }: TemporaryPasswordFieldProps) {
  const [status, setStatus] = useState<string>('');

  useEffect(() => setStatus(''), [password]);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(password);
      setStatus('Contraseña copiada al portapapeles.');
      onCopied();
    } catch {
      setStatus('No se pudo copiar. Usa el envío por email.');
    }
  };

  return (
    <div className="flex flex-col gap-1.5">
      <span className="text-[13px] font-semibold text-ink-2">Contraseña temporal</span>
      <div className="flex flex-wrap items-center gap-2">
        <span
          className="flex h-10 min-w-0 flex-1 items-center gap-2 rounded-lg border border-line-strong bg-surface-subtle px-3 font-mono text-sm tracking-[0.2em] text-ink-muted"
          aria-label="Contraseña temporal oculta, 16 caracteres"
        >
          <KeyRound size={15} className="shrink-0 tracking-normal" aria-hidden="true" />
          <span aria-hidden="true">{'•'.repeat(16)}</span>
        </span>
        <Button
          variant="secondary"
          size="sm"
          className="h-10"
          onClick={copy}
          leftIcon={copied ? <Check size={14} aria-hidden="true" /> : <Copy size={14} aria-hidden="true" />}
        >
          {copied ? 'Copiada' : 'Copiar'}
        </Button>
        <Button variant="ghost" size="sm" className="h-10" onClick={onRegenerate} leftIcon={<RefreshCw size={14} aria-hidden="true" />}>
          Regenerar
        </Button>
      </div>
      <p className="text-xs text-ink-muted">
        Generada automáticamente (16 caracteres). Nunca se muestra en pantalla; caduca en 72 h y el usuario debe cambiarla en su primer
        inicio de sesión.
      </p>
      <p aria-live="polite" className={error ? 'text-xs font-medium text-danger-text' : 'text-xs text-success-600'}>
        {error ?? status}
      </p>
    </div>
  );
}
