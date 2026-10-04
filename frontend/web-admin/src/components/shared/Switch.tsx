import { useId } from 'react';
import { cn } from '../../utils/cn';

export interface SwitchProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label: string;
  description?: string;
  hideLabel?: boolean;
  disabled?: boolean;
}

/** Interruptor accesible (`role="switch"`). */
export function Switch({ checked, onChange, label, description, hideLabel, disabled }: SwitchProps) {
  const id = useId();
  return (
    <div className="flex items-start gap-3">
      <button
        id={id}
        type="button"
        role="switch"
        aria-checked={checked}
        aria-label={hideLabel ? label : undefined}
        aria-labelledby={hideLabel ? undefined : `${id}-label`}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={cn(
          'relative mt-0.5 h-6 w-10 shrink-0 rounded-full transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-600 disabled:cursor-not-allowed disabled:opacity-50',
          checked ? 'bg-success-600' : 'bg-line-strong',
        )}
      >
        <span
          className={cn(
            'absolute left-0 top-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform',
            checked ? 'translate-x-[18px]' : 'translate-x-0.5',
          )}
        />
      </button>
      {!hideLabel && (
        <div className="flex flex-col">
          <span id={`${id}-label`} className="text-sm font-medium text-ink">
            {label}
          </span>
          {description && <span className="text-[13px] text-ink-muted">{description}</span>}
        </div>
      )}
    </div>
  );
}
