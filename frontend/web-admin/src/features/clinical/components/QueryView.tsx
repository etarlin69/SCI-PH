import type { ReactNode } from 'react';
import { ErrorState, LoadingSpinner } from '../../../components/shared';
import type { QueryState } from '../hooks/useClinicalQuery';

export interface QueryViewProps<T> {
    state: QueryState<T>;
    onRetry: () => void;
    errorTitle: string;
    loadingLabel?: string;
    /** Si devuelve true se muestra `empty` en lugar de `children`. */
    isEmpty?: (data: T) => boolean;
    empty?: ReactNode;
    children: (data: T) => ReactNode;
}

/** Renderiza explícitamente loading / error / vacío / datos de una consulta clínica. */
export function QueryView<T>({ state, onRetry, errorTitle, loadingLabel = 'Cargando…', isEmpty, empty, children }: QueryViewProps<T>) {
    if (state.status === 'loading') {
        return (
            <div className="flex justify-center py-8">
                <LoadingSpinner label={loadingLabel} />
            </div>
        );
    }
    if (state.status === 'error') {
        return <ErrorState variant="banner" title={errorTitle} message={state.error} onRetry={onRetry} />;
    }
    if (isEmpty?.(state.data)) return <>{empty}</>;
    return <>{children(state.data)}</>;
}
