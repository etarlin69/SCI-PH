import { useCallback } from 'react';
import { useToast } from '../../../components/shared';
import { describeError, isSimulated, type ClinicalFeature } from '../api';

/** Confirmaciones de escrituras clínicas. Si la función aún es simulada lo dice explícitamente: nada se guardó en el backend. */
export function useClinicalFeedback(feature: ClinicalFeature) {
    const toast = useToast();
    const simulated = isSimulated(feature);

    const success = useCallback(
        (title: string) =>
            toast(
                simulated
                    ? { title: `Simulación: ${title}`, description: 'No se envió al backend; se perderá al recargar.', tone: 'info' }
                    : { title, tone: 'success' },
            ),
        [toast, simulated],
    );

    const failure = useCallback(
        (title: string, error: unknown) => toast({ title, description: describeError(error), tone: 'error' }),
        [toast],
    );

    return { success, failure };
}
