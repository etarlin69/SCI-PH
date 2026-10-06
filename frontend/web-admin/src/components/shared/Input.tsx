import { forwardRef, useId, type InputHTMLAttributes, type ReactNode } from 'react';
import { AlertCircle, AlertTriangle, Check } from 'lucide-react';
import { cn } from '../../utils/cn';

export type FieldState = 'default' | 'success' | 'warning' | 'error';

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  hint?: string;
  error?: string;
  warning?: string;
  success?: string;
  leftIcon?: ReactNode;
  hideLabel?: boolean;
  containerClassName?: string;
}

export const FIELD_BORDER: Record<FieldState, string> = {
  default: 'border-line-strong focus:border-primary-600 focus:ring-primary-200',
  success: 'border-success-600 focus:ring-success-50',
  warning: 'border-warning-600 focus:ring-warning-50',
  error: 'border-danger-600 ring-[3px] ring-danger-50 focus:ring-danger-50',
};

export const FIELD_MESSAGE: Record<FieldState, string> = {
  default: 'text-ink-muted',
  success: 'text-success-600',
  warning: 'text-warning-700',
  error: 'text-danger-text',
};

export function fieldState(error?: string, warning?: string, success?: string): FieldState {
  if (error) return 'error';
  if (warning) return 'warning';
  if (success) return 'success';
  return 'default';
}

/** Icono + mensaje bajo el campo; compartido por Input y Select. */
export function FieldMessage({ id, state, message }: { id: string; state: FieldState; message?: string }) {
  if (!message) return null;
  return (
    <p id={id} className={cn('flex items-center gap-1 text-xs', FIELD_MESSAGE[state])}>
      {state === 'error' && <AlertCircle size={12} aria-hidden="true" />}
      {state === 'warning' && <AlertTriangle size={12} aria-hidden="true" />}
      {state === 'success' && <Check size={12} aria-hidden="true" />}
      {message}
    </p>
  );
}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { label, hint, error, warning, success, leftIcon, hideLabel, id, className, containerClassName, disabled, required, ...rest },
  ref,
) {
  const autoId = useId();
  const inputId = id ?? autoId;
  const messageId = `${inputId}-msg`;
  const state = fieldState(error, warning, success);
  const message = error ?? warning ?? success ?? hint;

  return (
    <div className={cn('flex flex-col gap-1.5', containerClassName)}>
      <label
        htmlFor={inputId}
        className={cn('text-[13px] font-semibold', disabled ? 'text-ink-disabled' : 'text-ink-2', hideLabel && 'sr-only')}
      >
        {label}
        {required && <span aria-hidden="true"> *</span>}
      </label>
      <div className="relative">
        {leftIcon && (
          <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-muted" aria-hidden="true">
            {leftIcon}
          </span>
        )}
        <input
          ref={ref}
          id={inputId}
          disabled={disabled}
          required={required}
          aria-invalid={state === 'error' || undefined}
          aria-describedby={message ? messageId : undefined}
          className={cn(
            'h-10 w-full rounded-lg border bg-white px-3 text-sm text-ink transition-shadow placeholder:text-ink-muted/80',
            'focus:outline-none focus:ring-[3px]',
            disabled ? 'cursor-not-allowed border-line bg-surface-ground text-ink-disabled' : FIELD_BORDER[state],
            leftIcon && 'pl-9',
            (state === 'success' || state === 'error') && 'pr-9',
            className,
          )}
          {...rest}
        />
        {state === 'success' && (
          <Check size={18} className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-success-600" aria-hidden="true" />
        )}
        {state === 'error' && (
          <AlertCircle
            size={18}
            className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-danger-600"
            aria-hidden="true"
          />
        )}
      </div>
      <FieldMessage id={messageId} state={state} message={message} />
    </div>
  );
});
