import { useId } from 'react';
import type { ClinicStatus } from '../../../types';
import { cn } from '../../../utils/cn';
import { CLINIC_STATUSES } from '../filters';

export interface ClinicStatusFieldProps {
  value: ClinicStatus;
  onChange: (value: ClinicStatus) => void;
  disabled?: boolean;
}

/** Selector Activa/Inactiva en forma de botones segmentados (radios nativos: navegación con flechas). */
export function ClinicStatusField({ value, onChange, disabled }: ClinicStatusFieldProps) {
  const id = useId();
  return (
    <div role="radiogroup" aria-labelledby={`${id}-label`} aria-describedby={`${id}-hint`} className="flex flex-col gap-1.5">
      <span id={`${id}-label`} className="text-[13px] font-semibold text-ink-2">
        Estado
      </span>
      <div className="grid grid-cols-2 gap-1.5">
        {CLINIC_STATUSES.map((s) => (
          <label key={s} className="relative">
            <input
              type="radio"
              name={`${id}-estado`}
              value={s}
              checked={value === s}
              disabled={disabled}
              onChange={() => onChange(s)}
              className="peer sr-only"
            />
            <span
              className={cn(
                'flex h-10 cursor-pointer items-center justify-center rounded-lg border text-sm',
                'peer-focus-visible:outline peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-primary-600',
                'peer-disabled:cursor-not-allowed peer-disabled:opacity-60',
                value === s
                  ? 'border-primary-600 bg-primary-50 font-semibold text-primary-700'
                  : 'border-line-strong bg-white text-ink-2 hover:bg-surface-hover',
              )}
            >
              {s}
            </span>
          </label>
        ))}
      </div>
      <span id={`${id}-hint`} className="text-xs text-ink-muted">
        {value === 'Inactiva' ? 'No aparecerá como destino de traslados.' : 'Disponible como destino de traslados.'}
      </span>
    </div>
  );
}
