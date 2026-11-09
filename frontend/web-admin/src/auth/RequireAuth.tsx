import { useState, type ReactNode } from 'react';
import { Link, Navigate, Outlet, useLocation } from 'react-router-dom';
import { Button } from '../components/shared/Button';
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

/**
 * No se pudo comprobar la sesión (límite de peticiones, sin red, servidor caído). La sesión puede seguir siendo válida,
 * así que no se envía al login: se ofrece reintentar, o ir al login si el usuario lo prefiere.
 */
export function SessionUnavailable() {
  const { retrySession } = useAuth();
  const [retrying, setRetrying] = useState(false);
  const retry = async () => {
    setRetrying(true);
    try {
      await retrySession();
    } finally {
      setRetrying(false);
    }
  };
  return (
    <main className="flex min-h-screen items-center justify-center bg-surface-ground px-4">
      <div role="alert" className="flex max-w-md flex-col items-center gap-4 text-center">
        <h1 className="m-0 text-lg font-semibold text-ink">No pudimos comprobar tu sesión</h1>
        <p className="m-0 text-sm text-ink-muted">
          El servidor no respondió o recibió demasiadas peticiones. Tu sesión puede seguir activa: vuelve a intentarlo en unos
          segundos.
        </p>
        <div className="flex flex-wrap justify-center gap-3">
          <Button onClick={() => void retry()} disabled={retrying}>
            Reintentar
          </Button>
          <Link to="/login" className="self-center text-sm text-primary-700 underline">
            Ir a iniciar sesión
          </Link>
        </div>
      </div>
    </main>
  );
}

/** initializing → carga · unavailable → reintentar · unauthenticated → /login (recordando la ruta) · authenticated → contenido. */
export function RequireAuth({ children }: RequireAuthProps) {
  const { status, signOutReason } = useAuth();
  const location = useLocation();
  if (status === 'initializing') return <SessionLoading />;
  if (status === 'unavailable') return <SessionUnavailable />;
  if (status === 'unauthenticated') {
    const reason = signOutReason === 'expired' ? 'expired' : undefined;
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search, reason }} />;
  }
  return children ? <>{children}</> : <Outlet />;
}
