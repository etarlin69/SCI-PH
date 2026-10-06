import type { ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { moduleForPath, MODULES } from './navigation';

export interface StatusPageProps {
    code?: string;
    title: string;
    message: ReactNode;
}

// Pantalla de estado dentro del layout (en construcción, 404, 403): ocupa el área de contenido, no la ventana.
export function StatusPage({ code, title, message }: StatusPageProps) {
    const module = moduleForPath(useLocation().pathname);

    return (
        <section className="flex flex-col items-center px-4 py-16 text-center sm:py-24">
            {code && <p className="tabular m-0 text-sm font-semibold tracking-[0.08em] text-primary-600">{code}</p>}
            <h1 className="m-0 mt-2 text-2xl font-bold text-ink sm:text-3xl">{title}</h1>
            <p className="m-0 mt-3 max-w-md text-sm text-ink-muted">{message}</p>
            <Link
                to={MODULES[module].home}
                className="mt-8 rounded-lg bg-primary-600 px-6 py-2 text-sm font-medium text-white no-underline hover:bg-primary-700 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-600"
            >
                {module === 'hospital' ? 'Volver a Hospital' : 'Volver al Dashboard'}
            </Link>
        </section>
    );
}
