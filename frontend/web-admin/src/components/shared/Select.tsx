import { forwardRef, useId, type SelectHTMLAttributes } from 'react';
import { ChevronDown } from 'lucide-react';
import { cn } from '../../utils/cn';
import { FIELD_BORDER, FieldMessage, fieldState } from './Input';

export interface SelectOption<V extends string = string> {
    value: V;
    label: string;
    disabled?: boolean;
}

export interface SelectProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'children'> {
    label: string;
    options: SelectOption[];
    placeholder?: string;
    hint?: string;
    error?: string;
    hideLabel?: boolean;
    layout?: 'stacked' | 'inline';
    containerClassName?: string;
}

export const Select = forwardRef<HTMLSelectElement, SelectProps>(function Select(
    {
        label,
        options,
        placeholder,
        hint,
        error,
        hideLabel,
        layout = 'stacked',
        id,
        className,
        containerClassName,
        disabled,
        required,
        ...rest
    },
    ref,
) {
    const autoId = useId();
    const selectId = id ?? autoId;
    const messageId = `${selectId}-msg`;
    const state = fieldState(error);
    const message = error ?? hint;

    return (
        <div className={cn(layout === 'inline' ? 'flex items-center gap-2.5' : 'flex flex-col gap-1.5', containerClassName)}>
            <label
                htmlFor={selectId}
                className={cn(
                    layout === 'inline' ? 'text-[13px] text-ink-3' : 'text-[13px] font-semibold text-ink-2',
                    disabled && 'text-ink-disabled',
                    hideLabel && 'sr-only',
                )}
            >
                {label}
                {required && <span aria-hidden="true"> *</span>}
            </label>
            <div className="relative">
                <select
                    ref={ref}
                    id={selectId}
                    disabled={disabled}
                    required={required}
                    aria-invalid={state === 'error' || undefined}
                    aria-describedby={message ? messageId : undefined}
                    className={cn(
                        'h-10 w-full appearance-none rounded-lg border bg-white pl-3 pr-9 text-sm text-ink',
                        'focus:outline-none focus:ring-[3px]',
                        disabled ? 'cursor-not-allowed border-line bg-surface-ground text-ink-disabled' : FIELD_BORDER[state],
                        className,
                    )}
                    {...rest}
                >
                    {placeholder !== undefined && <option value="">{placeholder}</option>}
                    {options.map((o) => (
                        <option key={o.value} value={o.value} disabled={o.disabled}>
                            {o.label}
                        </option>
                    ))}
                </select>
                <ChevronDown
                    size={16}
                    className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-ink-3"
                    aria-hidden="true"
                />
            </div>
            {layout === 'stacked' && <FieldMessage id={messageId} state={state} message={message} />}
        </div>
    );
});
