import type { ClinicalContext, ClinicSummary, Guid } from '../types/clinical.types';
import { describeError } from './clinicalApi';

/**
 * Clínica activa del usuario (D1 del backend): con una sola afiliación el BFF la resuelve solo y no se envía clinicId;
 * con varias, el usuario elige y se envía en cada operación con contexto de clínica. El BFF valida SIEMPRE el clinicId
 * contra las afiliaciones activas: esto es solo la preferencia de la UI. Vive en memoria (nunca en storage) y se
 * reinicia al cambiar de usuario.
 */
export interface ClinicSelectionSnapshot {
    userId: string | null;
    status: 'idle' | 'loading' | 'ready' | 'error';
    clinics: ClinicSummary[];
    selectedClinicId: Guid | null;
    error: string | null;
}

const IDLE: ClinicSelectionSnapshot = { userId: null, status: 'idle', clinics: [], selectedClinicId: null, error: null };

let snapshot = IDLE;
const listeners = new Set<() => void>();

function publish(next: ClinicSelectionSnapshot) {
    snapshot = next;
    listeners.forEach((listener) => listener());
}

export function subscribeClinicSelection(listener: () => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
}

export const getClinicSelection = (): ClinicSelectionSnapshot => snapshot;

/** Carga el contexto clínico una vez por usuario. Un 403 (no es personal clínico) queda como estado de error. */
export function ensureClinicalContext(userId: string, load: () => Promise<ClinicalContext>): void {
    if (snapshot.userId === userId && snapshot.status !== 'idle') return;
    publish({ ...IDLE, userId, status: 'loading' });
    load().then(
        (context) => {
            if (snapshot.userId !== userId) return;
            publish({ userId, status: 'ready', clinics: context.clinics, selectedClinicId: context.clinics[0]?.id ?? null, error: null });
        },
        (error: unknown) => {
            if (snapshot.userId !== userId) return;
            publish({ ...IDLE, userId, status: 'error', error: describeError(error) });
        },
    );
}

export function selectClinic(clinicId: Guid): void {
    if (snapshot.clinics.some((clinic) => clinic.id === clinicId)) publish({ ...snapshot, selectedClinicId: clinicId });
}

/** clinicId a enviar: solo cuando hay varias clínicas; con una, el backend la resuelve. */
export function clinicIdForRequest(): Guid | undefined {
    return snapshot.clinics.length > 1 ? (snapshot.selectedClinicId ?? undefined) : undefined;
}

/** Solo para tests. */
export function resetClinicSelectionForTests(): void {
    publish(IDLE);
}
