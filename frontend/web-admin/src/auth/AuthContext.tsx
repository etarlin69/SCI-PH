import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { refreshAccessToken, setAccessToken, setRefreshHandler } from '../api';
import { authApi } from './authApi';
import { openAuthChannel, waitForPeerRefresh, type AuthChannel, type AuthChannelEvent } from './authChannel';
import { refreshWithOneRetry, type RefreshAttempt } from './refreshFlow';
import type { MfaChallenge, MfaProof, WebSession, WebUser } from './types';

/**
 * `unavailable`: al arrancar no se pudo comprobar la sesión por un fallo transitorio (429, red, servidor) tras unos
 * pocos reintentos. La cookie puede seguir siendo válida: no se trata como sesión cerrada y se ofrece reintentar.
 */
export type AuthStatus = 'initializing' | 'authenticated' | 'unauthenticated' | 'unavailable';

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
  /** Vuelve a comprobar la sesión desde `unavailable` (botón «Reintentar»). */
  retrySession: () => Promise<void>;
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

export interface AuthProviderProps {
  children: ReactNode;
  /** Esperas entre los reintentos del arranque ante un fallo transitorio; su número acota los intentos. */
  startupRetryDelaysMs?: readonly number[];
  /** Espera máxima al `session-refreshed` de otra pestaña antes de reintentar un refresh `refresh-superseded`. */
  peerRefreshWaitMs?: number;
}

/** 3 intentos en total (~4 s más lo que tarden las respuestas); después, `unavailable` con «Reintentar». */
export const DEFAULT_STARTUP_RETRY_DELAYS_MS: readonly number[] = [1000, 3000];
export const DEFAULT_PEER_REFRESH_WAIT_MS = 1500;
/** Un Retry-After mayor no alarga la pantalla de carga: se pasa a `unavailable`. */
const MAX_STARTUP_WAIT_MS = 10_000;

const AuthContext = createContext<AuthContextValue | null>(null);

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

/**
 * Estado de autenticación de la SPA. El access token vive en memoria (aquí y en el cliente HTTP); el refresh token
 * solo en la cookie HttpOnly. Al arrancar intenta recuperar la sesión con POST /refresh: mientras tanto el estado es
 * `initializing` y no se renderiza contenido protegido.
 *
 * Varias pestañas comparten la cookie: el cliente HTTP serializa los refresh con un Web Lock y las pestañas se avisan de
 * los cambios de sesión por BroadcastChannel (solo eventos, nunca credenciales).
 */
export function AuthProvider({
  children,
  startupRetryDelaysMs = DEFAULT_STARTUP_RETRY_DELAYS_MS,
  peerRefreshWaitMs = DEFAULT_PEER_REFRESH_WAIT_MS,
}: AuthProviderProps) {
  const [state, setState] = useState<State>({ status: 'initializing', session: null, challenge: null, signOutReason: null });
  const mounted = useRef(true);
  const statusRef = useRef<AuthStatus>('initializing');
  statusRef.current = state.status;
  // Si había sesión, un refresh rechazado significa "caducó"; si no la había (arranque sin cookie), no hay aviso.
  const hasSession = useRef(false);
  const channel = useRef<AuthChannel | null>(null);
  // Resultado del último refresh: el arranque distingue "sin sesión" de "no se pudo comprobar".
  const lastAttempt = useRef<RefreshAttempt | null>(null);

  const broadcast = useCallback((event: AuthChannelEvent) => channel.current?.post(event), []);

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

  // Recupera la sesión desde la cookie (tras F5 o desde `unavailable`). Ante un fallo transitorio reintenta unas pocas
  // veces y después queda en `unavailable`: nunca un spinner infinito, ni un cierre de sesión por un 429 o la red.
  const restoreSession = useCallback(
    async (isActive: () => boolean) => {
      for (let attempt = 0; ; attempt++) {
        lastAttempt.current = null;
        const token = await refreshAccessToken();
        if (!isActive()) return;
        if (token) {
          try {
            const user = await authApi.me();
            if (isActive()) setState((s) => (s.session ? { ...s, session: { ...s.session, user } } : s));
          } catch {
            // La sesión del refresh ya trae el usuario; si /me falla con 401 el cliente habrá cerrado la sesión.
          }
          return;
        }

        const result = lastAttempt.current as RefreshAttempt | null;
        if (result?.kind !== 'unavailable') {
          setState((s) => (s.status === 'initializing' ? { ...s, status: 'unauthenticated', signOutReason: null } : s));
          return;
        }

        const delay = startupRetryDelaysMs[attempt];
        const retryAfterMs = (result.retryAfterSeconds ?? 0) * 1000;
        if (delay === undefined || retryAfterMs > MAX_STARTUP_WAIT_MS) {
          setState((s) => (s.status === 'initializing' ? { ...s, status: 'unavailable' } : s));
          return;
        }
        await sleep(Math.max(delay, retryAfterMs));
        if (!isActive() || statusRef.current !== 'initializing') return;
      }
    },
    [startupRetryDelaysMs],
  );

  // Eventos de otras pestañas. Quien recibe nunca reenvía ni llama al backend para cerrar sesión: solo limpia su estado.
  useEffect(() => {
    const opened = openAuthChannel();
    channel.current = opened;
    const unsubscribe = opened.subscribe((event) => {
      if (!mounted.current) return;
      const status = statusRef.current;
      switch (event.type) {
        case 'logged-out':
          if (status === 'authenticated' || status === 'unavailable') clearSession('logout');
          break;
        case 'session-ended':
          if (status === 'authenticated' || status === 'unavailable') clearSession(event.reason);
          break;
        case 'session-refreshed':
          // La cookie ya es la nueva. Una pestaña autenticada no necesita nada (su access token sigue valiendo y su
          // próximo refresh enviará la cookie vigente); una que no pudo comprobar la sesión vuelve a intentarlo.
          if (status === 'unavailable') {
            setState((s) => (s.status === 'unavailable' ? { ...s, status: 'initializing' } : s));
            void restoreSession(() => mounted.current);
          }
          break;
      }
    });
    return () => {
      unsubscribe();
      opened.close();
      if (channel.current === opened) channel.current = null;
    };
  }, [clearSession, restoreSession]);

  // Cómo renueva el cliente HTTP el token (ante un 401 o al arrancar), siempre dentro del Web Lock del cliente.
  // Solo un 401/403 del refresh termina la sesión; un fallo transitorio no la borra.
  useEffect(() => {
    mounted.current = true;
    setRefreshHandler(async () => {
      const attempt = await refreshWithOneRetry(() => waitForPeerRefresh(channel.current, peerRefreshWaitMs));
      lastAttempt.current = attempt;
      if (attempt.kind === 'refreshed') {
        if (mounted.current) applySession(attempt.session);
        else setAccessToken(attempt.session.accessToken);
        broadcast({ type: 'session-refreshed' });
        return attempt.session.accessToken;
      }
      if (attempt.kind === 'ended') {
        const hadSession = hasSession.current;
        if (mounted.current) clearSession(hadSession ? 'expired' : null);
        else setAccessToken(null);
        // La cookie es común: si la sesión terminó aquí, terminó para todas las pestañas.
        if (hadSession) broadcast({ type: 'session-ended', reason: 'expired' });
      }
      return null;
    });
    return () => {
      mounted.current = false;
      setRefreshHandler(null);
    };
  }, [applySession, clearSession, broadcast, peerRefreshWaitMs]);

  // Restauración tras F5: el token en memoria se perdió, la cookie HttpOnly no. refreshAccessToken es single-flight,
  // así que el doble efecto de StrictMode produce un único POST /refresh.
  useEffect(() => {
    let active = true;
    void restoreSession(() => active);
    return () => {
      active = false;
    };
  }, [restoreSession]);

  const login = useCallback(
    async (identifier: string, password: string): Promise<LoginResult> => {
      const response = await authApi.login({ identifier, password });
      if (!response.mfaRequired && response.session) {
        applySession(response.session);
        broadcast({ type: 'session-refreshed' });
        return { mfaRequired: false, session: response.session };
      }
      if (response.mfaRequired && response.mfaChallenge) {
        const challenge = response.mfaChallenge;
        setState((s) => ({ ...s, challenge }));
        return { mfaRequired: true, challenge };
      }
      throw new Error('Respuesta de inicio de sesión inesperada.');
    },
    [applySession, broadcast],
  );

  const completeMfa = useCallback(
    async (proof: MfaProof) => {
      const challenge = state.challenge;
      if (!challenge) throw new Error('No hay ninguna verificación pendiente.');
      // El backend consume el desafío solo con un código correcto; un código erróneo lo libera (y cuenta para el
      // bloqueo). Nunca se reintenta automáticamente: cada intento lo inicia el usuario.
      const session = await authApi.verifyMfa({ challengeToken: challenge.token, ...proof } as Parameters<typeof authApi.verifyMfa>[0]);
      applySession(session);
      broadcast({ type: 'session-refreshed' });
      return session;
    },
    [state.challenge, applySession, broadcast],
  );

  const cancelMfa = useCallback(() => setState((s) => ({ ...s, challenge: null })), []);

  const logout = useCallback(async () => {
    try {
      await authApi.logout();
    } catch {
      // Aunque el backend falle, la sesión local termina igual; la cookie caduca sola.
    } finally {
      clearSession('logout');
      broadcast({ type: 'logged-out' });
    }
  }, [clearSession, broadcast]);

  const refreshSession = useCallback(async () => (await refreshAccessToken()) !== null, []);

  const retrySession = useCallback(async () => {
    if (statusRef.current !== 'unavailable') return;
    statusRef.current = 'initializing';
    setState((s) => (s.status === 'unavailable' ? { ...s, status: 'initializing' } : s));
    await restoreSession(() => mounted.current);
  }, [restoreSession]);

  const reloadUser = useCallback(async () => {
    const user = await authApi.me();
    setState((s) => (s.session ? { ...s, session: { ...s.session, user } } : s));
  }, []);

  const changePassword = useCallback(
    async (currentPassword: string, newPassword: string) => {
      await authApi.changePassword({ currentPassword, newPassword });
      clearSession('password-changed');
      broadcast({ type: 'session-ended', reason: 'password-changed' });
    },
    [clearSession, broadcast],
  );

  const endSession = useCallback(
    (reason: SignOutReason) => {
      clearSession(reason);
      broadcast(reason === 'logout' ? { type: 'logged-out' } : { type: 'session-ended', reason });
    },
    [clearSession, broadcast],
  );

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
      retrySession,
      reloadUser,
      changePassword,
      endSession,
      can,
      canAny,
      hasRole,
    }),
    [state, roles, permissions, login, completeMfa, cancelMfa, logout, refreshSession, retrySession, reloadUser, changePassword, endSession, can, canAny, hasRole],
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
