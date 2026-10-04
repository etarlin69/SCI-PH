import { Link, useNavigate } from 'react-router-dom';
import { ShieldOff } from 'lucide-react';
import { useAuth } from '../../../auth';
import { Button } from '../../../components/shared';

export interface ForbiddenPageProps {
  missingPermission?: string;
}

/** Error 403 dentro del layout (la URL no cambia). */
export function ForbiddenPage({ missingPermission }: ForbiddenPageProps) {
  const { user } = useAuth();
  const navigate = useNavigate();
  return (
    <div className="flex justify-center px-2 py-10 sm:py-16">
      <div className="flex max-w-lg flex-col items-center gap-4 text-center">
        <span className="flex h-14 w-14 items-center justify-center rounded-full bg-danger-50 text-danger-600">
          <ShieldOff size={26} aria-hidden="true" />
        </span>
        <p className="font-mono text-xs font-semibold tracking-[0.08em] text-danger-text">ERROR 403</p>
        <h1 className="text-xl font-semibold text-ink sm:text-2xl">No tienes permiso para ver esta página</h1>
        <p className="text-sm text-ink-3">
          Tu rol <strong>{user?.rol ?? '—'}</strong> no incluye{' '}
          {missingPermission ? (
            <>
              el permiso <code className="rounded bg-surface-hover px-1 font-mono text-[13px]">{missingPermission}</code>
            </>
          ) : (
            'este permiso'
          )}
          . Si lo necesitas para tu trabajo, pide a un administrador que lo añada a tu rol.
        </p>
        <div className="flex flex-wrap justify-center gap-2.5">
          <Button variant="secondary" onClick={() => navigate(-1)}>
            Volver
          </Button>
          <Link
            to="/admin/dashboard"
            className="inline-flex h-10 items-center rounded-lg bg-primary-600 px-4 text-sm font-semibold text-white no-underline hover:bg-primary-700"
          >
            Ir al dashboard
          </Link>
        </div>
        <p className="text-xs text-ink-muted">El intento de acceso quedó registrado en auditoría.</p>
      </div>
    </div>
  );
}

export default ForbiddenPage;
