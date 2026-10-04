import { FlaskConical } from 'lucide-react';

export interface DemoCredentialsHintProps {
  variant: 'login' | 'mfa' | 'reset' | 'activate' | 'confirm';
}

/** Pista de datos de prueba visible solo con mocks. */
export function DemoCredentialsHint({ variant }: DemoCredentialsHintProps) {
  return (
    <aside
      aria-label="Datos de prueba"
      className="flex w-full max-w-[420px] gap-2.5 rounded-lg border border-dashed border-warning-500/60 bg-warning-50 px-3.5 py-3 text-xs text-warning-700"
    >
      <FlaskConical size={16} className="mt-0.5 shrink-0" aria-hidden="true" />
      <div className="flex flex-col gap-1 leading-relaxed">
        <strong className="font-semibold">Prototipo con datos de prueba</strong>
        {variant === 'login' && (
          <>
            <span>
              <code className="font-mono">admin@scih.com</code> · <code className="font-mono">Clave2026!</code> (Administrador)
            </span>
            <span>
              <code className="font-mono">cruz@auditores.do</code> · <code className="font-mono">Clave2026!</code> (Auditor externo, para
              ver el 403)
            </span>
            <span>Cualquier otra clave cuenta como intento fallido (bloqueo a los 5).</span>
          </>
        )}
        {variant === 'mfa' && (
          <span>
            Código <code className="font-mono">123456</code> · recuperación <code className="font-mono">7KQ2-M9XD</code>
          </span>
        )}
        {variant === 'reset' && (
          <span>
            Enlaces: <code className="font-mono">?token=demo</code> (válido), <code className="font-mono">caducado</code>,{' '}
            <code className="font-mono">usado</code>. Emails que empiezan por <code className="font-mono">limite</code> simulan el límite de
            envíos.
          </span>
        )}
        {(variant === 'activate' || variant === 'confirm') && (
          <span>
            Enlaces: <code className="font-mono">?token=demo</code> (válido), <code className="font-mono">caducado</code>,{' '}
            <code className="font-mono">usado</code>.
          </span>
        )}
      </div>
    </aside>
  );
}
