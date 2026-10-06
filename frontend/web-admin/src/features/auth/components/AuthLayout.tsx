import type { ReactNode } from 'react';
import { Activity, ShieldCheck } from 'lucide-react';

export interface AuthLayoutProps {
  children: ReactNode;
  footer?: ReactNode;
}

/** Pantallas de acceso sin Sidebar. ≥ lg: panel de marca a la izquierda + tarjeta; móvil: una columna, campos de 48 px y texto de 16 px (evita el zoom automático en iOS). */
export function AuthLayout({ children, footer }: AuthLayoutProps) {
  return (
    <div className="flex min-h-screen bg-surface-ground">
      <aside className="hidden w-[44%] max-w-[620px] flex-col justify-between bg-nav p-12 text-nav-text lg:flex">
        <div className="flex items-center gap-3">
          <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-primary-600 text-white">
            <Activity size={22} aria-hidden="true" />
          </span>
          <span className="flex flex-col">
            <span className="text-lg font-bold tracking-[0.04em] text-white">SCI-PH</span>
            <span className="text-xs text-[#9FB0C3]">Sistema Clínico Inteligente</span>
          </span>
        </div>
        <div className="flex max-w-md flex-col gap-4">
          <h2 className="text-3xl font-semibold leading-tight text-white">Plataforma web SCI-PH</h2>
          <p className="text-[15px] leading-relaxed text-[#9FB0C3]">
            Acceso del personal hospitalario y de administración de la red prehospitalaria y hospitalaria.
          </p>
        </div>
        <p className="flex items-center gap-2 text-[13px] text-[#9FB0C3]">
          <ShieldCheck size={16} aria-hidden="true" />
          Acceso restringido a personal autorizado. Cada inicio de sesión queda registrado en auditoría.
        </p>
      </aside>

      <main className="flex flex-1 flex-col items-center justify-center gap-5 px-4 py-8 sm:px-8">
        <div className="flex items-center gap-2.5 lg:hidden">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-primary-600 text-white">
            <Activity size={20} aria-hidden="true" />
          </span>
          <span className="flex flex-col">
            <span className="text-base font-bold tracking-[0.04em] text-ink">SCI-PH</span>
            <span className="text-xs text-ink-muted">Sistema Clínico Inteligente</span>
          </span>
        </div>
        <div className="w-full max-w-[420px] rounded-2xl border border-line bg-white px-5 py-7 shadow-[0_8px_24px_rgba(13,31,51,0.06)] sm:px-8 sm:py-9">
          {children}
        </div>
        {footer}
      </main>
    </div>
  );
}
