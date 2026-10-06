import { forwardRef, useEffect, useId, useRef, type InputHTMLAttributes, type ReactNode } from 'react';
import { cn } from '../../utils/cn';

export interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label: ReactNode;
  description?: ReactNode;
  indeterminate?: boolean;
  variant?: 'plain' | 'card';
  containerClassName?: string;
}

export const Checkbox = forwardRef<HTMLInputElement, CheckboxProps>(function Checkbox(
  { label, description, indeterminate = false, variant = 'plain', id, checked, disabled, className, containerClassName, ...rest },
  ref,
) {
  const autoId = useId();
  const inputId = id ?? autoId;
  const innerRef = useRef<HTMLInputElement | null>(null);

  // `indeterminate` solo existe como propiedad del DOM, no como atributo HTML.
  useEffect(() => {
    if (innerRef.current) innerRef.current.indeterminate = indeterminate;
  }, [indeterminate]);

  const setRefs = (el: HTMLInputElement | null) => {
    innerRef.current = el;
    if (typeof ref === 'function') ref(el);
    else if (ref) ref.current = el;
  };

  return (
    <label
      htmlFor={inputId}
      className={cn(
        'flex items-start gap-2.5',
        disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer',
        variant === 'card' && 'rounded-lg border p-3',
        variant === 'card' && (checked ? 'border-primary-600 bg-primary-50' : 'border-line bg-white'),
        containerClassName,
      )}
    >
      <input
        ref={setRefs}
        id={inputId}
        type="checkbox"
        checked={checked}
        disabled={disabled}
        className={cn('mt-px h-[18px] w-[18px] shrink-0 accent-primary-600 disabled:cursor-not-allowed', className)}
        {...rest}
      />
      <span className="flex min-w-0 flex-col gap-0.5">
        <span className="text-sm text-ink">{label}</span>
        {description && <span className="text-xs leading-snug text-ink-muted">{description}</span>}
      </span>
    </label>
  );
});
