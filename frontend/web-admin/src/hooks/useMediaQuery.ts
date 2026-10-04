import { useEffect, useState } from 'react';

/** `true` mientras se cumpla la media query (p. ej. `'(min-width: 1024px)'`). */
export function useMediaQuery(query: string): boolean {
  const get = () => (typeof window !== 'undefined' && window.matchMedia ? window.matchMedia(query).matches : false);
  const [matches, setMatches] = useState(get);
  useEffect(() => {
    const mql = window.matchMedia(query);
    const onChange = () => setMatches(mql.matches);
    onChange();
    mql.addEventListener('change', onChange);
    return () => mql.removeEventListener('change', onChange);
  }, [query]);
  return matches;
}

/** < 640 px: vista móvil (hojas inferiores, menú desplegable). */
export const useIsMobile = () => !useMediaQuery('(min-width: 640px)');
