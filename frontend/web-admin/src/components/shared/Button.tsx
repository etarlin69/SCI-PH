import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from 'react';
import { cn } from '../../utils/cn';
import { LoadingSpinner } from './LoadingSpinner';

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';
export type ButtonSize = 'sm' | 'md';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
    variant?: ButtonVariant;
    size?: ButtonSize;
    loading?: boolean;
    loadingText?: string;
    leftIcon?: ReactNode;
    fullWidth?: boolean;
}

const VARIANTS: Record<ButtonVariant, string> = {
    primary:
        'bg-primary-600 text-white hover:bg-primary-700 active:bg-primary-800 active:shadow-inner disabled:bg-line-strong disabled:text-ink-3 focus-visible:outline-primary-600',
    secondary:
        'border border-line-strong bg-white font-medium text-ink hover:bg-surface-hover active:bg-surface-pressed disabled:border-line disabled:bg-surface-subtle disabled:text-ink-disabled focus-visible:outline-primary-600',
    danger: 'bg-danger-600 text-white hover:bg-danger-700 active:bg-danger-800 active:shadow-inner disabled:bg-[#EFD3D0] disabled:text-[#7A4540] focus-visible:outline-danger-600',
    ghost: 'bg-transparent text-primary-600 hover:bg-surface-hover active:bg-surface-pressed disabled:text-[#9AA6B4] focus-visible:outline-primary-600',
};

const SIZES: Record<ButtonSize, string> = {
    sm: 'h-9 gap-1.5 px-3 text-[13px]',
    md: 'h-10 gap-2 px-4 text-sm',
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
    {
        variant = 'primary',
        size = 'md',
        loading = false,
        loadingText,
        leftIcon,
        fullWidth,
        className,
        children,
        disabled,
        type = 'button',
        ...rest
    },
    ref,
) {
    return (
        <button
            ref={ref}
            type={type}
            disabled={disabled || loading}
            aria-busy={loading || undefined}
            className={cn(
                'inline-flex shrink-0 items-center justify-center whitespace-nowrap rounded-lg font-semibold transition-colors',
                'focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 disabled:cursor-not-allowed',
                VARIANTS[variant],
                SIZES[size],
                fullWidth && 'w-full',
                className,
            )}
            {...rest}
        >
            {loading ? <LoadingSpinner size="sm" className="text-current" /> : leftIcon}
            {children !== undefined && <span>{loading && loadingText ? loadingText : children}</span>}
        </button>
    );
});
