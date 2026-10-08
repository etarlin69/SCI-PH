import { Check, Dot } from 'lucide-react';
import { cn } from '../../../utils/cn';
import { PASSWORD_RULES, STRENGTH_LABELS, passwordStrength } from '../password';

export interface PasswordStrengthMeterProps {
  password: string;
  id?: string;
}

const BAR = ['bg-line', 'bg-danger-600', 'bg-warning-500', 'bg-success-600', 'bg-success-600'];
const TEXT = ['text-ink-muted', 'text-danger-text', 'text-warning-700', 'text-success-600', 'text-success-600'];

/** Barra de fuerza (4 niveles) + checklist de la política de contraseñas. */
export function PasswordStrengthMeter({ password, id }: PasswordStrengthMeterProps) {
  const level = passwordStrength(password);
  return (
    <div id={id} className="flex flex-col gap-2.5">
      <div className="flex items-center gap-2.5" aria-live="polite">
        <div className="grid flex-1 grid-cols-4 gap-1" aria-hidden="true">
          {[1, 2, 3, 4].map((n) => (
            <span key={n} className={cn('h-1.5 rounded-full', n <= level ? BAR[level] : 'bg-line')} />
          ))}
        </div>
        <span className={cn('min-w-[80px] text-right text-xs font-semibold', TEXT[level])}>
          {level > 0 && <span className="sr-only">Fuerza: </span>}
          {STRENGTH_LABELS[level]}
        </span>
      </div>
      <ul aria-label="Requisitos de la contraseña" className="grid grid-cols-1 gap-x-3 gap-y-1.5 sm:grid-cols-2">
        {PASSWORD_RULES.map((r) => {
          const ok = r.test(password);
          return (
            <li key={r.id} className={cn('flex items-center gap-1.5 text-xs', ok ? 'text-success-600' : 'text-ink-muted')}>
              {ok ? <Check size={14} aria-hidden="true" /> : <Dot size={14} aria-hidden="true" />}
              {r.label}
              <span className="sr-only">{ok ? '(cumplido)' : '(pendiente)'}</span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
