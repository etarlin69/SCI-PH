import { NavLink } from 'react-router-dom';
import { Activity, LogOut } from 'lucide-react';
import type { CurrentUser } from '../../types';
import { cn } from '../../utils/cn';
import { initials } from '../../utils/formatters';
import { NAV_SECTIONS } from './navigation';

export interface SidebarProps {
    user: CurrentUser;
    onLogout?: () => void;
}

export function Sidebar({ user, onLogout }: SidebarProps) {
    return (
        <nav
            aria-label="Navegación principal"
            className="sticky top-0 flex h-screen w-[72px] shrink-0 flex-col gap-6 bg-nav px-3 py-5 text-nav-text lg:w-60 lg:px-3.5"
        >
            <div className="flex items-center gap-2.5 px-2">
                <span className="flex h-[34px] w-[34px] shrink-0 items-center justify-center rounded-lg bg-primary-600 text-white">
                    <Activity size={20} aria-hidden="true" />
                </span>
                <span className="sr-only flex-col gap-0.5 lg:not-sr-only lg:flex">
                    <span className="text-base font-bold tracking-[0.04em] text-white">SCI-PH</span>
                    <span className="text-xs text-[#9FB0C3]">Administración y Analítica</span>
                </span>
            </div>

            {NAV_SECTIONS.map((section) => (
                <div key={section.title} className="flex flex-col gap-0.5">
                    <span className="hidden px-2.5 pb-1.5 text-[11px] font-semibold uppercase tracking-[0.08em] text-nav-muted lg:block">
                        {section.title}
                    </span>
                    {section.items.map(({ to, label, icon: Icon }) => (
                        <NavLink
                            key={to}
                            to={to}
                            end={to === '/'}
                            title={label}
                            className={({ isActive }) =>
                                cn(
                                    'flex min-h-10 items-center justify-center gap-2.5 rounded-md px-2.5 text-sm no-underline lg:justify-start',
                                    'focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-300',
                                    isActive
                                        ? 'bg-nav-active font-semibold text-white'
                                        : 'text-nav-text hover:bg-nav-hover hover:text-white',
                                )
                            }
                        >
                            <Icon size={18} className="shrink-0" aria-hidden="true" />
                            <span className="sr-only lg:not-sr-only">{label}</span>
                        </NavLink>
                    ))}
                </div>
            ))}

            <div className="mt-auto flex items-center gap-2.5 border-t border-nav-border px-1 pt-3 lg:px-2.5">
                <span className="hidden h-[34px] w-[34px] shrink-0 items-center justify-center rounded-full bg-[#2A4A6E] text-[13px] font-semibold text-white lg:flex">
                    {initials(user.nombre)}
                </span>
                <span className="sr-only min-w-0 flex-1 flex-col gap-0.5 lg:not-sr-only lg:flex">
                    <span className="truncate text-[13px] font-semibold text-white">{user.nombre}</span>
                    <span className="truncate text-xs text-[#9FB0C3]">{user.email}</span>
                </span>
                <button
                    type="button"
                    onClick={onLogout}
                    aria-label="Cerrar sesión"
                    className="flex h-8 w-8 shrink-0 items-center justify-center rounded-md text-nav-text hover:bg-nav-hover hover:text-white"
                >
                    <LogOut size={18} aria-hidden="true" />
                </button>
            </div>
        </nav>
    );
}
