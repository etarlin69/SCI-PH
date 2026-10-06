import type { ReactNode } from 'react';
import { cn } from '../../utils/cn';

export interface DetailItem {
  label: string;
  value: ReactNode;
  mono?: boolean;
}

export interface DetailListProps {
  items: DetailItem[];
  columns?: 1 | 2;
  className?: string;
}

/** Lista de pares etiqueta/valor (`<dl>`) para fichas de detalle. */
export function DetailList({ items, columns = 2, className }: DetailListProps) {
  return (
    <dl className={cn('grid gap-x-6 gap-y-4', columns === 2 ? 'grid-cols-1 sm:grid-cols-2' : 'grid-cols-1', className)}>
      {items.map((i) => (
        <div key={i.label} className="flex min-w-0 flex-col gap-0.5">
          <dt className="text-xs font-medium text-ink-muted">{i.label}</dt>
          <dd className={cn('break-words text-sm text-ink', i.mono && 'font-mono text-[13px]')}>{i.value}</dd>
        </div>
      ))}
    </dl>
  );
}
