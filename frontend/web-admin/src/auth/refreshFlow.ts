import { isApiError } from '../api';
import { authApi } from './authApi';
import type { WebSession } from './types';

/** `code` del ProblemDetails del 409 reintentable de POST /refresh (Web BFF AuthController.RefreshSupersededCode). */
export const REFRESH_SUPERSEDED_CODE = 'refresh-superseded';

export type RefreshAttempt =
  | { kind: 'refreshed'; session: WebSession }
  /** 401/403: la sesión terminó (token inválido, caducado, revocado o reutilizado). */
  | { kind: 'ended' }
  /** Fallo transitorio (429, red, 5xx, 409 repetido): la sesión puede seguir siendo válida. */
  | { kind: 'unavailable'; retryAfterSeconds: number | null };

/**
 * Solo el 409 de POST /refresh con `code: refresh-superseded`: otra petición rotó el mismo token mientras esta iba en
 * camino. Ningún otro 409 de la aplicación se reintenta.
 */
export function isRefreshSuperseded(error: unknown): boolean {
  return isApiError(error, 409) && error.problem?.code === REFRESH_SUPERSEDED_CODE;
}

/**
 * Un POST /refresh clasificado. Ante `refresh-superseded` se reintenta EXACTAMENTE una vez, después de `waitForPeer`
 * (el `session-refreshed` de la pestaña ganadora, o un tiempo máximo): para entonces el navegador ya guardó la cookie
 * nueva y el reintento la envía. Un segundo 409 no se reintenta: queda como fallo transitorio, sin cerrar la sesión.
 */
export async function refreshWithOneRetry(waitForPeer: () => Promise<void>): Promise<RefreshAttempt> {
  let retried = false;
  for (;;) {
    try {
      return { kind: 'refreshed', session: await authApi.refresh() };
    } catch (error) {
      if (isApiError(error, 401) || isApiError(error, 403)) return { kind: 'ended' };
      if (!retried && isRefreshSuperseded(error)) {
        retried = true;
        await waitForPeer();
        continue;
      }
      return { kind: 'unavailable', retryAfterSeconds: isApiError(error) ? error.retryAfterSeconds : null };
    }
  }
}
