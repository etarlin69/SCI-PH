import { useCallback, useEffect, useRef, useState } from 'react';
import { describeError } from '../api';

export type QueryState<T> = { status: 'loading' } | { status: 'error'; error: string } | { status: 'success'; data: T };

/**
 * Carga datos clínicos con estados explícitos (loading / error / success) y cancelación al desmontar.
 * Un error nunca se convierte en lista vacía: 401/403/500 llegan a la UI como error.
 * `key` identifica la consulta; si es null no se carga nada.
 */
export function useClinicalQuery<T>(key: string | null, load: (signal: AbortSignal) => Promise<T>) {
    const loadRef = useRef(load);
    loadRef.current = load;
    const [state, setState] = useState<QueryState<T>>({ status: 'loading' });
    const [version, setVersion] = useState(0);

    useEffect(() => {
        if (key === null) return;
        const controller = new AbortController();
        setState({ status: 'loading' });
        loadRef.current(controller.signal).then(
            (data) => {
                if (!controller.signal.aborted) setState({ status: 'success', data });
            },
            (error: unknown) => {
                if (!controller.signal.aborted) setState({ status: 'error', error: describeError(error) });
            },
        );
        return () => controller.abort();
    }, [key, version]);

    const reload = useCallback(() => setVersion((v) => v + 1), []);
    return { state, reload };
}
