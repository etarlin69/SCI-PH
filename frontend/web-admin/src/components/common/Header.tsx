import { Bell, ChevronRight } from 'lucide-react';

export interface HeaderProps {
    section?: string;
    page?: string;
    environmentLabel?: string;
    unreadNotifications?: number;
    onNotificationsClick?: () => void;
}

export function Header({ section, page, environmentLabel, unreadNotifications = 0, onNotificationsClick }: HeaderProps) {
    return (
        <header className="flex h-16 shrink-0 items-center justify-between border-b border-line bg-white px-8">
            <nav aria-label="Ruta de navegación">
                <ol className="flex items-center gap-2 text-[13px] text-ink-muted">
                    {section && (
                        <li className="flex items-center gap-2">
                            {section}
                            <ChevronRight size={14} aria-hidden="true" />
                        </li>
                    )}
                    {page && (
                        <li aria-current="page" className="font-medium text-ink">
                            {page}
                        </li>
                    )}
                </ol>
            </nav>
            <div className="flex items-center gap-3">
                {environmentLabel && (
                    <span className="rounded-full bg-warning-50 px-2.5 py-1 text-xs font-semibold text-warning-700">
                        {environmentLabel}
                    </span>
                )}
                <button
                    type="button"
                    onClick={onNotificationsClick}
                    aria-label={unreadNotifications ? `Notificaciones (${unreadNotifications} sin leer)` : 'Notificaciones'}
                    className="relative flex h-10 w-10 items-center justify-center rounded-lg border border-line bg-white text-ink-3 hover:bg-surface-hover focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
                >
                    <Bell size={18} aria-hidden="true" />
                    {unreadNotifications > 0 && (
                        <span className="absolute -right-1 -top-1 flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-danger-600 px-1 text-[11px] font-semibold text-white">
                            {unreadNotifications}
                        </span>
                    )}
                </button>
            </div>
        </header>
    );
}
