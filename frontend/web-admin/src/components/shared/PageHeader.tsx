import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';

export interface PageHeaderProps {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  back?: { to: string; label: string };
  meta?: ReactNode;
}

export function PageHeader({ title, description, actions, back, meta }: PageHeaderProps) {
  return (
    <div className="flex flex-col gap-3">
      {back && (
        <Link
          to={back.to}
          className="inline-flex w-fit items-center gap-1.5 rounded text-[13px] font-medium text-primary-600 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
        >
          <ArrowLeft size={14} aria-hidden="true" />
          {back.label}
        </Link>
      )}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex min-w-0 flex-col gap-1">
          <div className="flex flex-wrap items-center gap-2.5">
            <h1 className="text-xl font-semibold text-ink sm:text-2xl">{title}</h1>
            {meta}
          </div>
          {description && <p className="text-[13px] text-ink-muted sm:text-sm">{description}</p>}
        </div>
        {actions && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
      </div>
    </div>
  );
}
