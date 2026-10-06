import { useCallback } from 'react';
import { useToast } from '../../../components/shared';
import { CLINICAL_API_IS_MOCK, describeError } from '../api';

/** Confirmaciones de escrituras clínicas. En modo MOCK lo dice explícitamente: nada se guardó en el backend. */
export function useClinicalFeedback() {
    const toast = useToast();

    const success = useCallback(
        (title: string) =>
            toast(
                CLINICAL_API_IS_MOCK
                    ? { title: `Simulación: ${title}`, description: 'No se envió al backend; se perderá al recargar.', tone: 'info' }
                    : { title, tone: 'success' },
            ),
        [toast],
    );

    const failure = useCallback(
        (title: string, error: unknown) => toast({ title, description: describeError(error), tone: 'error' }),
        [toast],
    );

    return { success, failure };
}
