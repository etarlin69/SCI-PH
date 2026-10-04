import { Link } from 'react-router-dom';
import { Compass } from 'lucide-react';

/** Error 404 (ruta inexistente o registro eliminado). */
export function NotFoundPage() {
  return (
    <div className="flex justify-center px-2 py-10 sm:py-16">
      <div className="flex max-w-lg flex-col items-center gap-4 text-center">
        <span className="flex h-14 w-14 items-center justify-center rounded-full bg-primary-100 text-primary-600">
          <Compass size={26} aria-hidden="true" />
        </span>
        <p className="font-mono text-xs font-semibold tracking-[0.08em] text-primary-600">ERROR 404</p>
        <h1 className="text-xl font-semibold text-ink sm:text-2xl">No encontramos esta página</h1>
        <p className="text-sm text-ink-3">Puede que el enlace esté mal escrito o que el registro se haya eliminado.</p>
        <Link
          to="/admin/dashboard"
          className="inline-flex h-10 items-center rounded-lg bg-primary-600 px-4 text-sm font-semibold text-white no-underline hover:bg-primary-700"
        >
          Ir al dashboard
        </Link>
      </div>
    </div>
  );
}

export default NotFoundPage;
