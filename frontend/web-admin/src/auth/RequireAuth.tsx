import type { ReactNode } from 'react';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { LoadingSpinner } from '../components/shared/LoadingSpinner';
import { useAuth } from './AuthContext';

export interface RequireAuthProps {
  children?: ReactNode;
}

/** Pantalla de arranque mientras se recupera la sesión: evita mostrar contenido protegido antes de saber si hay sesión. */
export function SessionLoading() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-surface-ground" aria-busy="true">
      <LoadingSpinner size="lg" label="Comprobando la sesión…" />
    </main>
  );
}

/** initializing → carga · unauthenticated → /login (recordando la ruta) · authenticated → contenido. */
export function RequireAuth({ children }: RequireAuthProps) {
  const { status, signOutReason } = useAuth();
  const location = useLocation();
  if (status === 'initializing') return <SessionLoading />;
  if (status === 'unauthenticated') {
    const reason = signOutReason === 'expired' ? 'expired' : undefined;
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search, reason }} />;
  }
  return children ? <>{children}</> : <Outlet />;
}
