import { useEffect } from 'react';

/** Título de la pestaña: "<página> · SCI-PH Admin". */
export function useDocumentTitle(title: string | undefined): void {
  useEffect(() => {
    if (title) document.title = `${title} · SCI-PH Admin`;
  }, [title]);
}
