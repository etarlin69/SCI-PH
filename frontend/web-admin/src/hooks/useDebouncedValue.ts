import { useEffect, useState } from 'react';

/** Valor retrasado `ms` (búsquedas que filtran listas largas). */
export function useDebouncedValue<T>(value: T, ms = 200): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const t = window.setTimeout(() => setV(value), ms);
    return () => window.clearTimeout(t);
  }, [value, ms]);
  return v;
}
