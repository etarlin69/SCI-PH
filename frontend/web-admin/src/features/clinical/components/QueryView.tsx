import type { ReactNode } from 'react';
import { Inbox } from 'lucide-react';
import { ErrorState, LoadingSpinner } from '../../../components/shared';
import type { QueryState } from '../hooks/useClinicalQuery';

export function EmptyState({ title, description }: { title: string; description?: string }) {
    return (
        <div className="flex flex-col items-center gap-2 rounded-lg border border-dashed border-line px-4 py-8 text-center">
            <Inbox size={22} className="text-ink-muted" aria-hidden="true" />
            <p className="m-0 text-sm font-semibold text-ink-2">{title}</p>
            {description && <p className="m-0 max-w-sm text-[13px] text-ink-muted">{description}</p>}
        </div>
    );
}

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
        return <ErrorState variant="inline" title={errorTitle} description={state.error} onRetry={onRetry} />;
    }
    if (isEmpty?.(state.data)) return <>{empty}</>;
    return <>{children(state.data)}</>;
}
