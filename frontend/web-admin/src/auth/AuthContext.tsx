import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { authService } from '../services';
import type { LoginInput, MfaChallenge, MfaInput, ModuleKey, PermissionAction, Session, SessionUser } from '../types';

export interface AuthContextValue {
  user: SessionUser | null;
  challenge: MfaChallenge | null;
  login: (input: LoginInput) => Promise<MfaChallenge>;
  verifyMfa: (code: string, method: MfaInput['method']) => Promise<Session>;
  cancelChallenge: () => void;
  logout: () => Promise<void>;
  can: (module: ModuleKey, action?: PermissionAction) => boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

/** Estado de autenticación de la SPA. */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(() => authService.getSession());
  const [challenge, setChallenge] = useState<MfaChallenge | null>(null);

  const login = useCallback(async (input: LoginInput) => {
    const ch = await authService.login(input);
    setChallenge(ch);
    return ch;
  }, []);

  const verifyMfa = useCallback(
    async (code: string, method: MfaInput['method']) => {
      if (!challenge) throw new Error('No hay verificación pendiente.');
      const s = await authService.verifyMfa({ challengeId: challenge.challengeId, code, method });
      setChallenge(null);
      setSession(s);
      return s;
    },
    [challenge],
  );

  const logout = useCallback(async () => {
    await authService.logout();
    setSession(null);
  }, []);

  const can = useCallback(
    (module: ModuleKey, action: PermissionAction = 'read') => !!session?.user.permisos[module]?.includes(action),
    [session],
  );

  const value = useMemo<AuthContextValue>(
    () => ({ user: session?.user ?? null, challenge, login, verifyMfa, cancelChallenge: () => setChallenge(null), logout, can }),
    [session, challenge, login, verifyMfa, logout, can],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth debe usarse dentro de <AuthProvider>.');
  return ctx;
}

/** Atajo: `const canEdit = useCan('users', 'update')`. */
export function useCan(module: ModuleKey, action: PermissionAction = 'read'): boolean {
  return useAuth().can(module, action);
}
