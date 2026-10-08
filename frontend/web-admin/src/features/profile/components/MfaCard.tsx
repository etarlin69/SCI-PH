import { useState, type FormEvent } from 'react';
import { ShieldCheck } from 'lucide-react';
import { describeApiError, isApiError } from '../../../api';
import { authApi, useAuth, type MfaSetup } from '../../../auth';
import { Button, Card, CopyButton, FormError, Input } from '../../../components/shared';
import { QrCode } from '../../../components/security/QrCode';

type Step =
  | { kind: 'idle' }
  | { kind: 'setup'; setup: MfaSetup }
  | { kind: 'manage'; action: 'disable' | 'regenerate' | null }
  | { kind: 'codes'; codes: string[]; endsSession: boolean };

const invalidCode = (e: unknown) =>
  isApiError(e, 400) ? 'El código no es válido. Usa el código actual de tu app de autenticación.' : describeApiError(e);

/**
 * Verificación en dos pasos (POST /api/web/auth/mfa/*). GET /me no indica si está activa: «Configurar» llama a
 * mfa/setup y un 409 significa que ya lo está. Activar o desactivar cierra las sesiones en el backend, así que
 * la SPA termina la sesión local después. Los códigos de recuperación solo viven en este estado mientras se muestran.
 */
export function MfaCard() {
  const { endSession } = useAuth();
  const [step, setStep] = useState<Step>({ kind: 'idle' });
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const reset = () => {
    setStep({ kind: 'idle' });
    setCode('');
    setError(null);
  };

  const startSetup = async () => {
    setBusy(true);
    setError(null);
    try {
      setStep({ kind: 'setup', setup: await authApi.mfaSetup() });
    } catch (e) {
      if (isApiError(e, 409)) setStep({ kind: 'manage', action: null });
      else setError(describeApiError(e));
    } finally {
      setBusy(false);
    }
  };

  const submitCode = async (e: FormEvent) => {
    e.preventDefault();
    if (busy || !/^\d{6}$/.test(code.trim())) {
      setError('Escribe los 6 dígitos del código.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      if (step.kind === 'setup') {
        const { codes } = await authApi.mfaEnable(code.trim());
        setStep({ kind: 'codes', codes, endsSession: true });
      } else if (step.kind === 'manage' && step.action === 'regenerate') {
        const { codes } = await authApi.mfaRegenerateRecoveryCodes(code.trim());
        setStep({ kind: 'codes', codes, endsSession: false });
      } else if (step.kind === 'manage' && step.action === 'disable') {
        await authApi.mfaDisable(code.trim());
        endSession('mfa-changed');
        return;
      }
      setCode('');
    } catch (err) {
      setError(invalidCode(err));
    } finally {
      setBusy(false);
    }
  };

  const codeForm = (label: string, danger = false) => (
    <form noValidate onSubmit={submitCode} className="flex flex-col gap-3">
      <Input
        label="Código de 6 dígitos de tu app"
        inputMode="numeric"
        autoComplete="one-time-code"
        maxLength={6}
        value={code}
        onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
        className="font-mono tracking-widest"
      />
      <FormError message={error} />
      <div className="flex flex-wrap gap-2">
        <Button type="submit" variant={danger ? 'danger' : 'primary'} loading={busy} loadingText="Verificando…">
          {label}
        </Button>
        <Button variant="secondary" onClick={reset} disabled={busy}>
          Cancelar
        </Button>
      </div>
    </form>
  );

  return (
    <Card title="Verificación en dos pasos" subtitle="Protege tu cuenta con un código de una app de autenticación (TOTP).">
      {step.kind === 'idle' && (
        <div className="flex flex-col gap-3">
          <FormError message={error} />
          <Button variant="secondary" leftIcon={<ShieldCheck size={16} aria-hidden="true" />} loading={busy} onClick={() => void startSetup()}>
            Configurar o gestionar
          </Button>
        </div>
      )}

      {step.kind === 'setup' && (
        <div className="flex flex-col gap-4">
          <p className="text-[13px] text-ink-3">1. Escanea el código con tu app de autenticación o introduce la clave manualmente.</p>
          <div className="flex flex-col items-start gap-3 sm:flex-row">
            <QrCode value={step.setup.authenticatorUri} size={168} label="Código QR para la app de autenticación" className="rounded border border-line" />
            <div className="flex min-w-0 flex-col gap-1.5">
              <span className="text-[13px] font-semibold text-ink-2">Clave manual</span>
              <code className="break-all rounded bg-surface-subtle px-2 py-1.5 font-mono text-sm">{step.setup.sharedKey}</code>
              <CopyButton value={step.setup.sharedKey} label="Copiar clave" />
            </div>
          </div>
          <p className="text-[13px] text-ink-3">2. Escribe el código que muestra la app para confirmar.</p>
          {codeForm('Activar verificación')}
        </div>
      )}

      {step.kind === 'manage' && (
        <div className="flex flex-col gap-3">
          <p className="text-[13px] text-ink-3">La verificación en dos pasos ya está activa en tu cuenta.</p>
          {step.action === null ? (
            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => setStep({ kind: 'manage', action: 'regenerate' })}>
                Generar nuevos códigos de recuperación
              </Button>
              <Button variant="danger" onClick={() => setStep({ kind: 'manage', action: 'disable' })}>
                Desactivar
              </Button>
              <Button variant="ghost" onClick={reset}>
                Cerrar
              </Button>
            </div>
          ) : step.action === 'regenerate' ? (
            codeForm('Generar códigos')
          ) : (
            <>
              <p className="text-[13px] text-danger-text">Desactivarla reduce la seguridad de tu cuenta y cerrará tus sesiones.</p>
              {codeForm('Desactivar verificación', true)}
            </>
          )}
        </div>
      )}

      {step.kind === 'codes' && (
        <div className="flex flex-col gap-3">
          <p role="status" className="rounded-lg bg-warning-50 px-3 py-2 text-[13px] text-warning-700">
            Guarda estos códigos de recuperación en un lugar seguro. Cada uno sirve una sola vez y no volverán a mostrarse.
          </p>
          <ol className="grid grid-cols-2 gap-1.5 font-mono text-sm">
            {step.codes.map((c) => (
              <li key={c} className="rounded bg-surface-subtle px-2 py-1">
                {c}
              </li>
            ))}
          </ol>
          <CopyButton value={step.codes.join('\n')} label="Copiar todos los códigos" />
          <Button
            onClick={() => {
              if (step.endsSession) endSession('mfa-changed');
              else reset();
            }}
          >
            {step.endsSession ? 'Los he guardado: volver a iniciar sesión' : 'Los he guardado'}
          </Button>
        </div>
      )}
    </Card>
  );
}
