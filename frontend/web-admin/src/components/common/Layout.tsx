import { useCallback, useState } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../auth';
// Solo la bandera (no el adaptador mock), para no cargar datos simulados en el chunk principal.
import { CLINICAL_API_IS_MOCK } from '../../features/clinical/api/mode';
import { useDocumentTitle } from '../../hooks/useDocumentTitle';
import { useToast } from '../shared';
import { Header, type Crumb } from './Header';
import { MobileNav } from './MobileNav';
import { PageMetaProvider, usePageMeta } from './PageMeta';
import { Sidebar } from './Sidebar';
import { MODULE_NAV, findNavEntry, moduleForPath } from './navigation';

/** Estructura general: Sidebar (rail/completo) o menú desplegable en móvil + Header + `<Outlet />`. */
export function Layout() {
  return (
    <PageMetaProvider>
      <LayoutInner />
    </PageMetaProvider>
  );
}

function LayoutInner() {
  const { user, canAny, logout } = useAuth();
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const toast = useToast();
  const { crumb } = usePageMeta();
  const [menuOpen, setMenuOpen] = useState(false);
  const closeMenu = useCallback(() => setMenuOpen(false), []);

  const module = moduleForPath(pathname);
  const nav = MODULE_NAV[module];
  const entry = findNavEntry(pathname);
  const rootCrumb: Crumb = module === 'hospital' ? { label: 'Hospital', to: nav.home } : { label: 'Dashboard', to: nav.home };
  const crumbs: Crumb[] = [rootCrumb];
  if (entry && entry.to !== nav.home) {
    // La sección se omite si repite la raíz (p. ej. «Hospital › Hospital › Recepción»).
    if (entry.section !== rootCrumb.label) crumbs.push({ label: entry.section });
    crumbs.push({ label: entry.page, to: entry.to });
  }
  // Hospital: el API clínico aún es un adaptador simulado. Admin: pantallas DEMO sin API.
  const environmentLabel = module === 'hospital' ? (CLINICAL_API_IS_MOCK ? 'Simulación' : undefined) : entry?.demo ? 'Datos de demostración' : undefined;
  if (crumb) crumbs.push({ label: crumb });
  useDocumentTitle(crumb ?? entry?.page ?? 'SCI-PH');

  if (!user) return null;

  const handleLogout = async () => {
    setMenuOpen(false);
    await logout();
    toast({ title: 'Sesión cerrada', tone: 'info' });
    navigate('/login', { replace: true });
  };

  const sidebarProps = { module, user, canAny, onLogout: () => void handleLogout() };

  return (
    <div className="flex min-h-screen bg-surface-ground">
      <a
        href="#main"
        className="sr-only z-toast rounded-md bg-white px-3 py-2 text-sm font-semibold text-primary-600 focus:not-sr-only focus:fixed focus:left-3 focus:top-3"
      >
        Saltar al contenido
      </a>
      <Sidebar {...sidebarProps} />
      <MobileNav open={menuOpen} onClose={closeMenu}>
        <Sidebar {...sidebarProps} variant="drawer" onNavigate={closeMenu} />
      </MobileNav>
      <div className="flex min-w-0 flex-1 flex-col">
        <Header
          crumbs={crumbs}
          environmentLabel={environmentLabel}
          onMenuClick={() => setMenuOpen(true)}
          menuOpen={menuOpen}
        />
        <main id="main" tabIndex={-1} className="flex-1 px-4 pb-8 pt-5 focus:outline-none sm:px-6 sm:pt-6 lg:px-8 lg:pt-7">
          {entry?.demo && (
            <p
              role="note"
              className="mb-5 rounded-lg border border-warning-500/40 bg-warning-50 px-3.5 py-2.5 text-[13px] text-warning-700"
            >
              <strong className="font-semibold">Pantalla de demostración.</strong> Muestra datos ficticios: este módulo aún no
              tiene API en el backend y nada de lo que hagas aquí se guarda.
            </p>
          )}
          <Outlet />
        </main>
      </div>
    </div>
  );
}
