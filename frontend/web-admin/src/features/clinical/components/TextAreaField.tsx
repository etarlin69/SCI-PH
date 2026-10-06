import { forwardRef, useId, type TextareaHTMLAttributes } from 'react';
import { FIELD_BORDER, FieldMessage, fieldState } from '../../../components/shared';
import { cn } from '../../../utils/cn';

export interface TextAreaFieldProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
    label: string;
    hint?: string;
    error?: string;
}

// Texto largo con la misma anatomía que Input (label, mensaje y estados) del sistema compartido.
export const TextAreaField = forwardRef<HTMLTextAreaElement, TextAreaFieldProps>(function TextAreaField(
    { label, hint, error, id, className, disabled, required, rows = 3, ...rest },
    ref,
) {
    const autoId = useId();
    const fieldId = id ?? autoId;
    const messageId = `${fieldId}-msg`;
    const state = fieldState(error);
    const message = error ?? hint;

    return (
        <div className="flex flex-col gap-1.5">
            <label htmlFor={fieldId} className={cn('text-[13px] font-semibold', disabled ? 'text-ink-disabled' : 'text-ink-2')}>
                {label}
                {required && <span aria-hidden="true"> *</span>}
            </label>
            <textarea
                ref={ref}
                id={fieldId}
                rows={rows}
                disabled={disabled}
                required={required}
                aria-invalid={state === 'error' || undefined}
                aria-describedby={message ? messageId : undefined}
                className={cn(
                    'w-full rounded-lg border bg-white px-3 py-2 text-sm text-ink placeholder:text-ink-muted/80',
                    'focus:outline-none focus:ring-[3px]',
                    disabled ? 'cursor-not-allowed border-line bg-surface-ground text-ink-disabled' : FIELD_BORDER[state],
                    className,
                )}
                {...rest}
            />
            <FieldMessage id={messageId} state={state} message={message} />
        </div>
    );
});
