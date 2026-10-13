import { useCallback, useEffect, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import { errorMessage } from '../services';

export interface AsyncState<T> {
  data: T | undefined;
  loading: boolean;
  error: string | null;
  reload: () => void;
  setData: Dispatch<SetStateAction<T | undefined>>;
}

/** Carga datos de un servicio y expone loading/error/reload. */
export function useAsync<T>(fn: () => Promise<T>, deps: unknown[] = []): AsyncState<T> {
  const [data, setData] = useState<T>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [nonce, setNonce] = useState(0);
  const fnRef = useRef(fn);
  fnRef.current = fn;

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);
    fnRef.current().then(
      (value) => {
        if (!active) return;
        setData(value);
        setLoading(false);
      },
      (err: unknown) => {
        if (!active) return;
        setError(errorMessage(err));
        setLoading(false);
      },
    );
    return () => {
      active = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nonce, ...deps]);

  const reload = useCallback(() => setNonce((n) => n + 1), []);
  return { data, loading, error, reload, setData };
}
