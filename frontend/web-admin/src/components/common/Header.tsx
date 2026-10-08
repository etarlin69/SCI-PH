import { Link } from 'react-router-dom';
import { Bell, ChevronRight, Menu } from 'lucide-react';

export interface Crumb {
  label: string;
  to?: string;
}

export interface HeaderProps {
  crumbs: Crumb[];
  environmentLabel?: string;
  unreadNotifications?: number;
  onNotificationsClick?: () => void;
  onMenuClick: () => void;
  menuOpen: boolean;
}

export function Header({ crumbs, environmentLabel, unreadNotifications = 0, onNotificationsClick, onMenuClick, menuOpen }: HeaderProps) {
  const current = crumbs[crumbs.length - 1];
  return (
    <header className="sticky top-0 z-popover flex h-14 shrink-0 items-center justify-between gap-3 border-b border-line bg-white px-4 sm:h-16 sm:px-6 lg:px-8">
      <div className="flex min-w-0 items-center gap-2">
        <button
          type="button"
          onClick={onMenuClick}
          aria-label="Abrir menú"
          aria-expanded={menuOpen}
          aria-controls="mobile-nav"
          className="-ml-1.5 flex h-10 w-10 shrink-0 items-center justify-center rounded-lg text-ink-3 hover:bg-surface-hover focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600 sm:hidden"
        >
          <Menu size={20} aria-hidden="true" />
        </button>
        <nav aria-label="Ruta de navegación" className="min-w-0">
          {/* Móvil: solo la página actual. */}
          <span className="block truncate text-[15px] font-semibold text-ink sm:hidden">{current?.label}</span>
          <ol className="hidden min-w-0 items-center gap-2 text-[13px] text-ink-muted sm:flex">
            {crumbs.map((c, i) => {
              const last = i === crumbs.length - 1;
              return (
                <li key={`${c.label}-${i}`} className="flex min-w-0 items-center gap-2" aria-current={last ? 'page' : undefined}>
                  {last ? (
                    <span className="truncate font-medium text-ink">{c.label}</span>
                  ) : c.to ? (
                    <Link to={c.to} className="truncate text-ink-muted no-underline hover:text-primary-600 hover:underline">
                      {c.label}
                    </Link>
                  ) : (
                    <span className="truncate">{c.label}</span>
                  )}
                  {!last && <ChevronRight size={14} className="shrink-0" aria-hidden="true" />}
                </li>
              );
            })}
          </ol>
        </nav>
      </div>
      <div className="flex shrink-0 items-center gap-2 sm:gap-3">
        {environmentLabel && (
          <span className="hidden rounded-full bg-warning-50 px-2.5 py-1 text-xs font-semibold text-warning-700 sm:inline">
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
