import { forwardRef, useId, useState, type InputHTMLAttributes } from 'react';
import { Eye, EyeOff } from 'lucide-react';
import { cn } from '../../../utils/cn';
import { FIELD_BORDER, FieldMessage, fieldState } from '../../../components/shared';

export interface PasswordInputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label: string;
  error?: string;
  success?: string;
  hint?: string;
}

/** Campo de contraseña con botón mostrar/ocultar (`aria-pressed`). */
export const PasswordInput = forwardRef<HTMLInputElement, PasswordInputProps>(function PasswordInput(
  { label, error, success, hint, id, className, ...rest },
  ref,
) {
  const autoId = useId();
  const inputId = id ?? autoId;
  const [visible, setVisible] = useState(false);
  const state = fieldState(error, undefined, success);
  const message = error ?? success ?? hint;

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={inputId} className="text-[13px] font-semibold text-ink-2">
        {label}
      </label>
      <div className="relative">
        <input
          ref={ref}
          id={inputId}
          type={visible ? 'text' : 'password'}
          aria-invalid={state === 'error' || undefined}
          aria-describedby={message ? `${inputId}-msg` : undefined}
          className={cn(
            'h-12 w-full rounded-lg border bg-white pl-3.5 pr-12 text-base text-ink focus:outline-none focus:ring-2 sm:h-11 sm:text-[15px]',
            FIELD_BORDER[state],
            className,
          )}
          {...rest}
        />
        <button
          type="button"
          onClick={() => setVisible((v) => !v)}
          aria-pressed={visible}
          aria-label={visible ? 'Ocultar contraseña' : 'Mostrar contraseña'}
          className="absolute right-1.5 top-1/2 flex h-9 w-9 -translate-y-1/2 items-center justify-center rounded-md text-ink-muted hover:bg-surface-hover hover:text-ink focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
        >
          {visible ? <EyeOff size={18} aria-hidden="true" /> : <Eye size={18} aria-hidden="true" />}
        </button>
      </div>
      <FieldMessage id={`${inputId}-msg`} state={state} message={message} />
    </div>
  );
});
