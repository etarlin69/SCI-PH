import { ChevronRight } from 'lucide-react';

export interface HeaderProps {
    section?: string;
    page?: string;
    environmentLabel?: string;
}

export function Header({ section, page, environmentLabel }: HeaderProps) {
    return (
        <header className="flex h-16 shrink-0 items-center justify-between gap-3 border-b border-line bg-white px-4 sm:px-6 lg:px-8">
            <nav aria-label="Ruta de navegación" className="min-w-0">
                <ol className="m-0 flex min-w-0 list-none items-center gap-2 p-0 text-[13px] text-ink-muted">
                    {section && (
                        <li className="flex items-center gap-2">
                            {section}
                            <ChevronRight size={14} aria-hidden="true" />
                        </li>
                    )}
                    {page && (
                        <li aria-current="page" className="truncate font-medium text-ink">
                            {page}
                        </li>
                    )}
                </ol>
            </nav>
            {environmentLabel && (
                <span className="shrink-0 rounded-full bg-warning-50 px-2.5 py-1 text-xs font-semibold text-warning-700">
                    {environmentLabel}
                </span>
            )}
        </header>
    );
}
