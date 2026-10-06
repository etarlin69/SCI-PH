import { forwardRef, useId, type TextareaHTMLAttributes } from 'react';
import { cn } from '../../utils/cn';
import { FIELD_BORDER, FieldMessage, fieldState } from './Input';

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label: string;
  hint?: string;
  error?: string;
  showCount?: boolean;
  count?: number;
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea(
  { label, hint, error, showCount, count, id, className, maxLength, value, ...rest },
  ref,
) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  const state = fieldState(error);
  const length = count ?? (typeof value === 'string' ? value.length : 0);
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-baseline justify-between gap-2">
        <label htmlFor={fieldId} className="text-[13px] font-semibold text-ink-2">
          {label}
        </label>
        {showCount && maxLength && (
          <span className="tabular text-xs text-ink-muted" aria-hidden="true">
            {length}/{maxLength}
          </span>
        )}
      </div>
      <textarea
        ref={ref}
        id={fieldId}
        value={value}
        maxLength={maxLength}
        aria-invalid={state === 'error' || undefined}
        aria-describedby={error || hint ? `${fieldId}-msg` : undefined}
        className={cn(
          'min-h-[88px] w-full rounded-lg border bg-white px-3 py-2 text-sm text-ink placeholder:text-ink-muted focus:outline-none focus:ring-2 focus:ring-primary-200',
          FIELD_BORDER[state],
          className,
        )}
        {...rest}
      />
      <FieldMessage id={`${fieldId}-msg`} state={state} message={error ?? hint} />
    </div>
  );
});
