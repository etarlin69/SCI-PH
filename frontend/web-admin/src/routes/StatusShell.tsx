import { Outlet } from 'react-router-dom';
import { useAuth } from '../auth';
import { Layout } from '../components/common';

/** Contenedor de las páginas de estado públicas (`/403`, `/404`, rutas desconocidas): con sesión se ven dentro del layout del portal; sin sesión, en una pantalla limpia. */
export function StatusShell() {
  const { user } = useAuth();
  if (user) return <Layout />;
  return (
    <main className="flex min-h-screen items-center justify-center bg-surface-ground px-4">
      <Outlet />
    </main>
  );
}
