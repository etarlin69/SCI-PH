import { Link, useNavigate } from 'react-router-dom';
import { ShieldOff } from 'lucide-react';
import { resolveHomeRoute, roleLabel, useAuth } from '../../../auth';
import { Button } from '../../../components/shared';

export interface ForbiddenPageProps {
  missingPermissions?: readonly string[];
}

/** Error 403: el usuario está autenticado pero no tiene permiso. No cierra la sesión ni cambia la URL. */
export function ForbiddenPage({ missingPermissions }: ForbiddenPageProps) {
  const { user, permissions } = useAuth();
  const navigate = useNavigate();
  const home = user ? resolveHomeRoute(permissions) : '/login';

  return (
    <div className="flex justify-center px-2 py-10 sm:py-16">
      <div className="flex max-w-lg flex-col items-center gap-4 text-center">
        <span className="flex h-14 w-14 items-center justify-center rounded-full bg-danger-50 text-danger-600">
          <ShieldOff size={26} aria-hidden="true" />
        </span>
        <p className="font-mono text-xs font-semibold tracking-[0.08em] text-danger-text">ERROR 403</p>
        <h1 className="text-xl font-semibold text-ink sm:text-2xl">No tienes permiso para ver esta página</h1>
        <p className="text-sm text-ink-3">
          {user && user.roles.length > 0 && (
            <>
              Tus roles ({user.roles.map(roleLabel).join(', ')}) no incluyen{' '}
            </>
          )}
          {!user || user.roles.length === 0 ? 'Tu cuenta no incluye ' : null}
          {missingPermissions && missingPermissions.length > 0 ? (
            <>
              {missingPermissions.length === 1 ? 'el permiso ' : 'ninguno de los permisos '}
              {missingPermissions.map((p, i) => (
                <span key={p}>
                  {i > 0 && ', '}
                  <code className="rounded bg-surface-hover px-1 font-mono text-[13px]">{p}</code>
                </span>
              ))}
            </>
          ) : (
            'este permiso'
          )}
          . Si lo necesitas para tu trabajo, pide a un administrador que revise tus roles.
        </p>
        <div className="flex flex-wrap justify-center gap-2.5">
          <Button variant="secondary" onClick={() => navigate(-1)}>
            Volver
          </Button>
          {home !== '/403' && (
            <Link
              to={home}
              className="inline-flex h-10 items-center rounded-lg bg-primary-600 px-4 text-sm font-semibold text-white no-underline hover:bg-primary-700"
            >
              Ir al inicio
            </Link>
          )}
        </div>
      </div>
    </div>
  );
}

export default ForbiddenPage;
