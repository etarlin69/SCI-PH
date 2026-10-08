import { apiRequest } from '../api';
import type {
  ActivateAccountRequest,
  ChangePasswordRequest,
  ConfirmEmailRequest,
  MfaSetup,
  MfaVerifyRequest,
  RecoveryCodes,
  ResetPasswordRequest,
  WebLoginRequest,
  WebLoginResponse,
  WebSession,
  WebUser,
} from './types';

const BASE = '/api/web/auth';

/**
 * Endpoints de autenticación del Web BFF. Los que leen o fijan la cookie de refresh (login, mfa/verify, refresh,
 * logout) llevan `X-SCIPH-CSRF: 1` y van sin Bearer ni refresh automático: un 401 ahí es un resultado, no una
 * sesión caducada.
 */
export const authApi = {
  login: (request: WebLoginRequest) =>
    apiRequest<WebLoginResponse>(`${BASE}/login`, { method: 'POST', body: request, auth: false, csrf: true }),

  verifyMfa: (request: MfaVerifyRequest) =>
    apiRequest<WebSession>(`${BASE}/mfa/verify`, { method: 'POST', body: request, auth: false, csrf: true }),

  refresh: () => apiRequest<WebSession>(`${BASE}/refresh`, { method: 'POST', auth: false, csrf: true }),

  logout: () => apiRequest<void>(`${BASE}/logout`, { method: 'POST', auth: false, csrf: true }),

  me: (signal?: AbortSignal) => apiRequest<WebUser>(`${BASE}/me`, { signal }),

  /** 204 y el backend borra la cookie y revoca las sesiones: hay que volver a iniciar sesión. */
  changePassword: (request: ChangePasswordRequest) => apiRequest<void>(`${BASE}/change-password`, { method: 'POST', body: request }),

  revokeOwnSessions: () => apiRequest<void>(`${BASE}/revoke-sessions`, { method: 'POST' }),

  /** 202 genérico: no revela si el email existe. */
  forgotPassword: (email: string) =>
    apiRequest<void>(`${BASE}/forgot-password`, { method: 'POST', body: { email }, auth: false }),

  resetPassword: (request: ResetPasswordRequest) =>
    apiRequest<void>(`${BASE}/reset-password`, { method: 'POST', body: request, auth: false }),

  /** El cliente solo presenta token + contraseña; sujeto, email y rol salen de la invitación guardada. */
  activateAccount: (request: ActivateAccountRequest) =>
    apiRequest<void>(`${BASE}/activate-account`, { method: 'POST', body: request, auth: false }),

  confirmEmail: (request: ConfirmEmailRequest) =>
    apiRequest<void>(`${BASE}/confirm-email`, { method: 'POST', body: request, auth: false }),

  resendConfirmation: (email: string) =>
    apiRequest<void>(`${BASE}/resend-confirmation`, { method: 'POST', body: { email }, auth: false }),

  mfaSetup: () => apiRequest<MfaSetup>(`${BASE}/mfa/setup`, { method: 'POST' }),

  /** Devuelve los códigos de recuperación (una sola vez) y el backend cierra la sesión actual. */
  mfaEnable: (code: string) => apiRequest<RecoveryCodes>(`${BASE}/mfa/enable`, { method: 'POST', body: { code } }),

  mfaDisable: (code: string) => apiRequest<void>(`${BASE}/mfa/disable`, { method: 'POST', body: { code } }),

  mfaRegenerateRecoveryCodes: (code: string) =>
    apiRequest<RecoveryCodes>(`${BASE}/mfa/recovery-codes/regenerate`, { method: 'POST', body: { code } }),
};
