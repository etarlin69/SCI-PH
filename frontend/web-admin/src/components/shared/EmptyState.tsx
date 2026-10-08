import type { ReactNode } from 'react';
import type { LucideIcon } from 'lucide-react';
import { Inbox } from 'lucide-react';
import { cn } from '../../utils/cn';

export interface EmptyStateProps {
  icon?: LucideIcon;
  title: string;
  description?: ReactNode;
  action?: ReactNode;
  className?: string;
  titleAs?: 'p' | 'h1' | 'h2';
}

/** Estado vacío: lista sin datos o búsqueda sin resultados. */
export function EmptyState({ icon: Icon = Inbox, title, description, action, className, titleAs: Title = 'p' }: EmptyStateProps) {
  return (
    <div className={cn('flex flex-col items-center gap-3 px-4 py-12 text-center', className)}>
      <span className="flex h-12 w-12 items-center justify-center rounded-full bg-surface-hover text-ink-muted">
        <Icon size={22} aria-hidden="true" />
      </span>
      <div className="flex max-w-sm flex-col gap-1">
        <Title className="text-[15px] font-semibold text-ink">{title}</Title>
        {description && <p className="text-[13px] text-ink-muted">{description}</p>}
      </div>
      {action && <div className="mt-1 flex flex-wrap justify-center gap-2">{action}</div>}
    </div>
  );
}
