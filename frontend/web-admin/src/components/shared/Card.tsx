import { useId, type HTMLAttributes, type ReactNode } from 'react';
import { cn } from '../../utils/cn';

export interface CardProps extends Omit<HTMLAttributes<HTMLElement>, 'title'> {
    title?: ReactNode;
    subtitle?: ReactNode;
    actions?: ReactNode;
    padding?: 'none' | 'md';
    as?: 'section' | 'div' | 'article';
}

export function Card({ title, subtitle, actions, padding = 'md', as: Tag = 'section', className, children, ...rest }: CardProps) {
    const id = useId();
    const titleId = title ? `${id}-title` : undefined;

    return (
        <Tag
            aria-labelledby={Tag === 'div' ? undefined : titleId}
            className={cn('rounded-[10px] border border-line bg-white', padding === 'md' && 'px-6 pb-6 pt-5', className)}
            {...rest}
        >
            {(title || actions) && (
                <div className="mb-4 flex items-start justify-between gap-4">
                    <div className="flex min-w-0 flex-col gap-1">
                        {title && (
                            <h2 id={titleId} className="text-base font-semibold text-ink">
                                {title}
                            </h2>
                        )}
                        {subtitle && <p className="text-[13px] text-ink-muted">{subtitle}</p>}
                    </div>
                    {actions}
                </div>
            )}
            {children}
        </Tag>
    );
}
