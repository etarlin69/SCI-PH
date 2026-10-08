import { AlertTriangle, RotateCw } from 'lucide-react';
import { cn } from '../../utils/cn';
import { Button } from './Button';

export interface ErrorStateProps {
  title?: string;
  message: string;
  onRetry?: () => void;
  variant?: 'block' | 'banner';
  className?: string;
}

/** Error de carga con acción "Reintentar". */
export function ErrorState({ title = 'No se pudieron cargar los datos', message, onRetry, variant = 'block', className }: ErrorStateProps) {
  if (variant === 'banner') {
    return (
      <div
        role="alert"
        className={cn(
          'flex flex-wrap items-center gap-3 rounded-lg border border-danger-600/30 bg-danger-50 px-4 py-3 text-[13px] text-danger-text',
          className,
        )}
      >
        <AlertTriangle size={18} className="shrink-0" aria-hidden="true" />
        <span className="min-w-0 flex-1">
          <strong className="font-semibold">{title}.</strong> {message}
        </span>
        {onRetry && (
          <Button size="sm" variant="secondary" leftIcon={<RotateCw size={14} aria-hidden="true" />} onClick={onRetry}>
            Reintentar
          </Button>
        )}
      </div>
    );
  }
  return (
    <div role="alert" className={cn('flex flex-col items-center gap-3 px-4 py-12 text-center', className)}>
      <span className="flex h-12 w-12 items-center justify-center rounded-full bg-danger-50 text-danger-600">
        <AlertTriangle size={22} aria-hidden="true" />
      </span>
      <div className="flex max-w-md flex-col gap-1">
        <p className="text-[15px] font-semibold text-ink">{title}</p>
        <p className="text-[13px] text-ink-muted">{message}</p>
      </div>
      {onRetry && (
        <Button variant="secondary" leftIcon={<RotateCw size={16} aria-hidden="true" />} onClick={onRetry}>
          Reintentar
        </Button>
      )}
    </div>
  );
}
