import { NavLink } from 'react-router-dom';
import { Activity, LogOut } from 'lucide-react';
import type { ModuleKey, SessionUser } from '../../types';
import { cn } from '../../utils/cn';
import { initials } from '../../utils/formatters';
import { NAV_SECTIONS } from './navigation';

export interface SidebarProps {
  user: SessionUser;
  canRead: (module: ModuleKey) => boolean;
  onLogout: () => void;
  variant?: 'rail' | 'drawer';
  onNavigate?: () => void;
}

/** Menú lateral. */
export function Sidebar({ user, canRead, onLogout, variant = 'rail', onNavigate }: SidebarProps) {
  const drawer = variant === 'drawer';
  const showLabel = drawer ? '' : 'sr-only lg:not-sr-only';
  const sections = NAV_SECTIONS.map((s) => ({ ...s, items: s.items.filter((i) => !i.module || canRead(i.module)) })).filter(
    (s) => s.items.length > 0,
  );

  return (
    <nav
      aria-label="Navegación principal"
      className={cn(
        'flex flex-col gap-6 overflow-y-auto bg-nav py-5 text-nav-text',
        drawer ? 'h-full w-[280px] px-3.5' : 'sticky top-0 hidden h-screen w-[72px] shrink-0 px-3 sm:flex lg:w-60 lg:px-3.5',
      )}
    >
      <div className="flex items-center gap-2.5 px-2">
        <span className="flex h-[34px] w-[34px] shrink-0 items-center justify-center rounded-lg bg-primary-600 text-white">
          <Activity size={20} aria-hidden="true" />
        </span>
        <span className={cn('flex-col gap-0.5', drawer ? 'flex' : 'sr-only lg:not-sr-only lg:flex')}>
          <span className="text-base font-bold tracking-[0.04em] text-white">SCI-PH</span>
          <span className="text-xs text-[#9FB0C3]">Administración y Analítica</span>
        </span>
      </div>

      {sections.map((section) => (
        <div key={section.title} className="flex flex-col gap-0.5">
          <span
            className={cn(
              'px-2.5 pb-1.5 text-[11px] font-semibold uppercase tracking-[0.08em] text-nav-muted',
              drawer ? 'block' : 'hidden lg:block',
            )}
          >
            {section.title}
          </span>
          {section.items.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/'}
              title={drawer ? undefined : label}
              onClick={onNavigate}
              className={({ isActive }) =>
                cn(
                  'flex min-h-10 items-center gap-2.5 rounded-md px-2.5 text-sm no-underline',
                  drawer ? 'justify-start' : 'justify-center lg:justify-start',
                  'focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-300',
                  isActive ? 'bg-nav-active font-semibold text-white' : 'text-nav-text hover:bg-nav-hover hover:text-white',
                )
              }
            >
              <Icon size={18} className="shrink-0" aria-hidden="true" />
              <span className={showLabel}>{label}</span>
            </NavLink>
          ))}
        </div>
      ))}

      <div
        className={cn(
          'mt-auto flex items-center gap-2.5 border-t border-nav-border pt-3',
          drawer ? 'px-2.5' : 'flex-col px-1 lg:flex-row lg:px-2.5',
        )}
      >
        <span
          className={cn(
            'h-[34px] w-[34px] shrink-0 items-center justify-center rounded-full bg-[#2A4A6E] text-[13px] font-semibold text-white',
            drawer ? 'flex' : 'hidden lg:flex',
          )}
          aria-hidden="true"
        >
          {initials(user.nombre)}
        </span>
        <span className={cn('min-w-0 flex-1 flex-col gap-0.5', drawer ? 'flex' : 'sr-only lg:not-sr-only lg:flex')}>
          <span className="truncate text-[13px] font-semibold text-white">{user.nombre}</span>
          <span className="truncate text-xs text-[#9FB0C3]">
            {user.rol} · {user.email}
          </span>
        </span>
        <button
          type="button"
          onClick={onLogout}
          aria-label="Cerrar sesión"
          title="Cerrar sesión"
          className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md text-nav-text hover:bg-nav-hover hover:text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-300"
        >
          <LogOut size={18} aria-hidden="true" />
        </button>
      </div>
    </nav>
  );
}
