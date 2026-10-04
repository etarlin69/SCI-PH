import { cn } from '../../utils/cn';

export type SpinnerSize = 'sm' | 'md' | 'lg';

export interface LoadingSpinnerProps {
    size?: SpinnerSize;
    label?: string;
    className?: string;
}

const SIZE_PX: Record<SpinnerSize, number> = { sm: 16, md: 24, lg: 36 };

export function LoadingSpinner({ size = 'md', label, className }: LoadingSpinnerProps) {
    const px = SIZE_PX[size];
    return (
        <span role={label ? 'status' : undefined} className={cn('inline-flex items-center gap-2 text-primary-600', className)}>
            <svg width={px} height={px} viewBox="0 0 24 24" fill="none" className="shrink-0 animate-spin" aria-hidden="true">
                <circle cx="12" cy="12" r="9" stroke="currentColor" strokeOpacity="0.25" strokeWidth="3" />
                <path d="M21 12a9 9 0 0 0-9-9" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
            </svg>
            {label && <span className="text-sm text-ink-3">{label}</span>}
        </span>
    );
}
