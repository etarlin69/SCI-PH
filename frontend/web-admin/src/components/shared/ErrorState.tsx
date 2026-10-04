import { AlertTriangle, RefreshCw } from 'lucide-react';
import { cn } from '../../utils/cn';
import { Button } from './Button';

export interface ErrorStateProps {
    title: string;
    description?: string;
    onRetry?: () => void;
    retrying?: boolean;
    // 'panel' ocupa el área del contenido; 'inline' es un aviso compacto sobre datos que siguen visibles.
    variant?: 'panel' | 'inline';
    className?: string;
}

// Mensaje de error para el usuario: nunca muestra detalles técnicos ni trazas.
export function ErrorState({ title, description, onRetry, retrying = false, variant = 'panel', className }: ErrorStateProps) {
    const retry = onRetry && (
        <Button
            variant="secondary"
            size="sm"
            onClick={onRetry}
            loading={retrying}
            loadingText="Reintentando…"
            leftIcon={<RefreshCw size={16} aria-hidden="true" />}
        >
            Reintentar
        </Button>
    );

    if (variant === 'inline') {
        return (
            <div
                role="alert"
                className={cn(
                    'flex flex-wrap items-center gap-3 rounded-[10px] border border-danger-50 bg-danger-50 px-4 py-3 text-sm text-danger-text',
                    className,
                )}
            >
                <AlertTriangle size={18} className="shrink-0" aria-hidden="true" />
                <span className="min-w-0 flex-1">
                    <span className="font-semibold">{title}</span>
                    {description && <span> {description}</span>}
                </span>
                {retry}
            </div>
        );
    }

    return (
        <div role="alert" className={cn('flex flex-col items-center gap-3 px-4 py-16 text-center', className)}>
            <span className="flex h-12 w-12 items-center justify-center rounded-full bg-danger-50 text-danger-600">
                <AlertTriangle size={22} aria-hidden="true" />
            </span>
            <h2 className="m-0 text-lg font-semibold text-ink">{title}</h2>
            {description && <p className="m-0 max-w-md text-sm text-ink-muted">{description}</p>}
            {retry}
        </div>
    );
}
