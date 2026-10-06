import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { isApiError, refreshAccessToken, setAccessToken, setRefreshHandler } from '../api';
import { authApi } from './authApi';
import type { MfaChallenge, MfaProof, WebSession, WebUser } from './types';

export type AuthStatus = 'initializing' | 'authenticated' | 'unauthenticated';

/** Motivo del último cierre de sesión, para el aviso del login. */
export type SignOutReason = 'logout' | 'expired' | 'password-changed' | 'mfa-changed';

export type LoginResult = { mfaRequired: false; session: WebSession } | { mfaRequired: true; challenge: MfaChallenge };

export interface AuthContextValue {
  status: AuthStatus;
  user: WebUser | null;
  roles: readonly string[];
  permissions: readonly string[];
  accessTokenExpiresAtUtc: string | null;
  /** Desafío MFA pendiente (solo memoria; nunca se persiste). */
  mfaChallenge: MfaChallenge | null;
  signOutReason: SignOutReason | null;
  login: (identifier: string, password: string) => Promise<LoginResult>;
  /** Un 401 no distingue código erróneo, desafío caducado o bloqueo: la UI decide si reintentar o volver al login. */
  completeMfa: (proof: MfaProof) => Promise<WebSession>;
  cancelMfa: () => void;
  logout: () => Promise<void>;
  refreshSession: () => Promise<boolean>;
  /** Recarga el usuario desde GET /me (fuente de verdad de roles y permisos). */
  reloadUser: () => Promise<void>;
  /** El backend revoca las sesiones al cambiar la contraseña: aquí se cierra la sesión local a continuación. */
  changePassword: (currentPassword: string, newPassword: string) => Promise<void>;
  /** Termina la sesión local tras una operación que revoca sesiones en el backend (p. ej. activar MFA). */
  endSession: (reason: SignOutReason) => void;
  can: (permission: string) => boolean;
  canAny: (permissions: readonly string[]) => boolean;
  hasRole: (role: string) => boolean;
}

interface State {
  status: AuthStatus;
  session: WebSession | null;
  challenge: MfaChallenge | null;
  signOutReason: SignOutReason | null;
}

const AuthContext = createContext<AuthContextValue | null>(null);

/**
 * Estado de autenticación de la SPA. El access token vive en memoria (aquí y en el cliente HTTP); el refresh token
 * solo en la cookie HttpOnly. Al arrancar intenta recuperar la sesión con POST /refresh: mientras tanto el estado es
 * `initializing` y no se renderiza contenido protegido.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<State>({ status: 'initializing', session: null, challenge: null, signOutReason: null });
  const mounted = useRef(true);
  // Si había sesión, un refresh rechazado significa "caducó"; si no la había (arranque sin cookie), no hay aviso.
  const hasSession = useRef(false);

  const applySession = useCallback((session: WebSession) => {
    hasSession.current = true;
    setAccessToken(session.accessToken);
    setState({ status: 'authenticated', session, challenge: null, signOutReason: null });
  }, []);

  const clearSession = useCallback((reason: SignOutReason | null) => {
    hasSession.current = false;
    setAccessToken(null);
    setState({ status: 'unauthenticated', session: null, challenge: null, signOutReason: reason });
  }, []);

  // Cómo renueva el cliente HTTP el token ante un 401. Solo un 401/403 del refresh termina la sesión;
  // un fallo de red no la borra (se reintentará en la próxima petición).
  useEffect(() => {
    mounted.current = true;
    setRefreshHandler(async () => {
      try {
        const session = await authApi.refresh();
        if (mounted.current) applySession(session);
        else setAccessToken(session.accessToken);
        return session.accessToken;
      } catch (error) {
        if (isApiError(error, 401) || isApiError(error, 403)) {
          if (mounted.current) clearSession(hasSession.current ? 'expired' : null);
          else setAccessToken(null);
        }
        return null;
      }
    });
    return () => {
      mounted.current = false;
      setRefreshHandler(null);
    };
  }, [applySession, clearSession]);

  // Restauración tras F5: el token en memoria se perdió, la cookie HttpOnly no. refreshAccessToken es single-flight,
  // así que el doble efecto de StrictMode produce un único POST /refresh.
  useEffect(() => {
    let active = true;
    void refreshAccessToken().then(async (token) => {
      if (!active) return;
      if (!token) {
        setState((s) => (s.status === 'initializing' ? { ...s, status: 'unauthenticated', signOutReason: null } : s));
        return;
      }
      try {
        const user = await authApi.me();
        if (active) setState((s) => (s.session ? { ...s, session: { ...s.session, user } } : s));
      } catch {
        // La sesión del refresh ya trae el usuario; si /me falla con 401 el cliente habrá cerrado la sesión.
      }
    });
    return () => {
      active = false;
    };
  }, []);

  const login = useCallback(
    async (identifier: string, password: string): Promise<LoginResult> => {
      const response = await authApi.login({ identifier, password });
      if (!response.mfaRequired && response.session) {
        applySession(response.session);
        return { mfaRequired: false, session: response.session };
      }
      if (response.mfaRequired && response.mfaChallenge) {
        const challenge = response.mfaChallenge;
        setState((s) => ({ ...s, challenge }));
        return { mfaRequired: true, challenge };
      }
      throw new Error('Respuesta de inicio de sesión inesperada.');
    },
    [applySession],
  );

  const completeMfa = useCallback(
    async (proof: MfaProof) => {
      const challenge = state.challenge;
      if (!challenge) throw new Error('No hay ninguna verificación pendiente.');
      // El backend consume el desafío solo con un código correcto; un código erróneo lo libera (y cuenta para el
      // bloqueo). Nunca se reintenta automáticamente: cada intento lo inicia el usuario.
      const session = await authApi.verifyMfa({ challengeToken: challenge.token, ...proof } as Parameters<typeof authApi.verifyMfa>[0]);
      applySession(session);
      return session;
    },
    [state.challenge, applySession],
  );

  const cancelMfa = useCallback(() => setState((s) => ({ ...s, challenge: null })), []);

  const logout = useCallback(async () => {
    try {
      await authApi.logout();
    } catch {
      // Aunque el backend falle, la sesión local termina igual; la cookie caduca sola.
    } finally {
      clearSession('logout');
    }
  }, [clearSession]);

  const refreshSession = useCallback(async () => (await refreshAccessToken()) !== null, []);

  const reloadUser = useCallback(async () => {
    const user = await authApi.me();
    setState((s) => (s.session ? { ...s, session: { ...s.session, user } } : s));
  }, []);

  const changePassword = useCallback(
    async (currentPassword: string, newPassword: string) => {
      await authApi.changePassword({ currentPassword, newPassword });
      clearSession('password-changed');
    },
    [clearSession],
  );

  const endSession = useCallback((reason: SignOutReason) => clearSession(reason), [clearSession]);

  const permissions = state.session?.user.permissions ?? EMPTY;
  const roles = state.session?.user.roles ?? EMPTY;
  const can = useCallback((permission: string) => permissions.includes(permission), [permissions]);
  const canAny = useCallback((list: readonly string[]) => list.some((p) => permissions.includes(p)), [permissions]);
  const hasRole = useCallback((role: string) => roles.includes(role), [roles]);

  const value = useMemo<AuthContextValue>(
    () => ({
      status: state.status,
      user: state.session?.user ?? null,
      roles,
      permissions,
      accessTokenExpiresAtUtc: state.session?.accessTokenExpiresAtUtc ?? null,
      mfaChallenge: state.challenge,
      signOutReason: state.signOutReason,
      login,
      completeMfa,
      cancelMfa,
      logout,
      refreshSession,
      reloadUser,
      changePassword,
      endSession,
      can,
      canAny,
      hasRole,
    }),
    [state, roles, permissions, login, completeMfa, cancelMfa, logout, refreshSession, reloadUser, changePassword, endSession, can, canAny, hasRole],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

const EMPTY: readonly string[] = [];

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth debe usarse dentro de <AuthProvider>.');
  return ctx;
}

/** Atajo: `const canManage = useCan('Users.Manage')`. */
export function useCan(permission: string): boolean {
  return useAuth().can(permission);
}

/** Atajo: alguno de los permisos. */
export function useCanAny(permissions: readonly string[]): boolean {
  return useAuth().canAny(permissions);
}
