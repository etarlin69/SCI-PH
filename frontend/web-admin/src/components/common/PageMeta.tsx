import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';

interface PageMetaValue {
  crumb: string | null;
  setCrumb: (crumb: string | null) => void;
}

const PageMetaContext = createContext<PageMetaValue>({ crumb: null, setCrumb: () => undefined });

export function PageMetaProvider({ children }: { children: ReactNode }) {
  const [crumb, setCrumb] = useState<string | null>(null);
  return <PageMetaContext.Provider value={{ crumb, setCrumb }}>{children}</PageMetaContext.Provider>;
}

export const usePageMeta = () => useContext(PageMetaContext);

/** Añade un nivel a las migas de pan del Header y al título de la pestaña: `useBreadcrumb(user?.nombre)` → "Accesos › Usuarios › Ana Jiménez". */
export function useBreadcrumb(label: string | undefined | null): void {
  const { setCrumb } = usePageMeta();
  useEffect(() => {
    setCrumb(label ?? null);
    return () => setCrumb(null);
  }, [label, setCrumb]);
}
