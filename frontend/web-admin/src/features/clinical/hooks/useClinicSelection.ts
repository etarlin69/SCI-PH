import { useEffect, useSyncExternalStore } from 'react';
import { useAuth } from '../../../auth';
import { clinicalApi } from '../api';
import { ensureClinicalContext, getClinicSelection, subscribeClinicSelection } from '../api/clinicSelection';

/**
 * Contexto de clínica del usuario actual. Las consultas con contexto de clínica (listas, admisión) deben esperar a
 * `ready` y usar `queryKey` en su clave para recargarse al cambiar de clínica.
 */
export function useClinicSelection() {
    const { user } = useAuth();
    const snapshot = useSyncExternalStore(subscribeClinicSelection, getClinicSelection);
    const userId = user?.id ?? null;

    useEffect(() => {
        if (userId) ensureClinicalContext(userId, () => clinicalApi.getClinicalContext());
    }, [userId]);

    const current = snapshot.userId === userId ? snapshot : null;
    return {
        ready: current?.status === 'ready',
        error: current?.status === 'error' ? current.error : null,
        clinics: current?.clinics ?? [],
        selectedClinicId: current?.selectedClinicId ?? null,
        /** Parte de la clave de las consultas con contexto de clínica. */
        queryKey: current?.selectedClinicId ?? 'none',
    };
}
