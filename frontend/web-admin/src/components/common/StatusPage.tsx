import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';

export interface StatusPageProps {
  code?: string;
  title: string;
  message: ReactNode;
}

export function StatusPage({ code, title, message }: StatusPageProps) {
  return (
    <section className="flex flex-col items-center px-4 py-16 text-center sm:py-24">
      {code && <p className="tabular m-0 text-sm font-semibold tracking-[0.08em] text-primary-600">{code}</p>}
      <h1 className="m-0 mt-2 text-2xl font-bold text-ink sm:text-3xl">{title}</h1>
      <p className="m-0 mt-3 max-w-md text-sm text-ink-muted">{message}</p>
      <Link
        to="/admin/dashboard"
        className="mt-8 rounded-lg bg-primary-600 px-6 py-2 text-sm font-medium text-white no-underline hover:bg-primary-700 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-600"
      >
        Volver al Dashboard
      </Link>
    </section>
  );
}
