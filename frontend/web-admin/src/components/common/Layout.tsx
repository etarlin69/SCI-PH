import { useCallback, useState } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../auth';
import { useDocumentTitle } from '../../hooks/useDocumentTitle';
import { useToast } from '../shared';
import { Header, type Crumb } from './Header';
import { MobileNav } from './MobileNav';
import { PageMetaProvider, usePageMeta } from './PageMeta';
import { Sidebar } from './Sidebar';
import { findNavEntry } from './navigation';

export interface LayoutProps {
  mockData?: boolean;
}

/** Estructura general: Sidebar (rail/completo) o menú desplegable en móvil + Header + `<Outlet />`. */
export function Layout(props: LayoutProps) {
  return (
    <PageMetaProvider>
      <LayoutInner {...props} />
    </PageMetaProvider>
  );
}

function LayoutInner({ mockData = true }: LayoutProps) {
  const { user, can, logout } = useAuth();
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const toast = useToast();
  const { crumb } = usePageMeta();
  const [menuOpen, setMenuOpen] = useState(false);
  const closeMenu = useCallback(() => setMenuOpen(false), []);

  const entry = findNavEntry(pathname);
  const crumbs: Crumb[] = [{ label: 'Dashboard', to: '/admin/dashboard' }];
  if (entry && entry.to !== '/admin/dashboard') {
    crumbs.push({ label: entry.section }, { label: entry.page, to: entry.to });
  }
  if (crumb) crumbs.push({ label: crumb });
  useDocumentTitle(crumb ?? entry?.page ?? 'SCI-PH');

  if (!user) return null;

  const handleLogout = async () => {
    setMenuOpen(false);
    await logout();
    toast({ title: 'Sesión cerrada', tone: 'info' });
    navigate('/login', { replace: true });
  };

  const sidebarProps = { user, canRead: (m: Parameters<typeof can>[0]) => can(m, 'read'), onLogout: () => void handleLogout() };

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
          environmentLabel={mockData ? 'Datos de prueba' : undefined}
          onMenuClick={() => setMenuOpen(true)}
          menuOpen={menuOpen}
        />
        <main id="main" tabIndex={-1} className="flex-1 px-4 pb-8 pt-5 focus:outline-none sm:px-6 sm:pt-6 lg:px-8 lg:pt-7">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
