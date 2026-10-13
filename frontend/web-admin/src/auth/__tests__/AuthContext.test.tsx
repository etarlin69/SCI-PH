import { StrictMode } from 'react';
import { act, render, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, getAccessToken, isApiError } from '../../api';
import { ADMIN_USER, NURSE_USER, createFakeBff, json, problem, session } from '../../test/fakeBff';
import { AuthProvider, useAuth, type AuthContextValue } from '../AuthContext';

let auth!: AuthContextValue;
function Probe() {
  auth = useAuth();
  return null;
}

function renderAuth() {
  return render(
    <StrictMode>
      <AuthProvider>
        <Probe />
      </AuthProvider>
    </StrictMode>,
  );
}

const noContent = () => new Response(null, { status: 204 });

describe('AuthProvider', () => {
  let storageWrites: ReturnType<typeof vi.spyOn>[];
  beforeEach(() => {
    storageWrites = [vi.spyOn(Storage.prototype, 'setItem')];
  });

  const expectNoStorageWrites = () => {
    for (const spy of storageWrites) expect(spy).not.toHaveBeenCalled();
    expect(document.cookie).toBe('');
  };

  it('restaura la sesión tras F5: initializing → POST refresh (un solo intento) → /me con Bearer → authenticated', async () => {
    const bff = createFakeBff({
      'POST /api/web/auth/refresh': () => json(200, session(ADMIN_USER, 'restored')),
      'GET /api/web/auth/me': () => json(200, ADMIN_USER),
    });
    renderAuth();
    expect(auth.status).toBe('initializing');

    await waitFor(() => expect(auth.status).toBe('authenticated'));
    await waitFor(() => expect(bff.calls('GET /api/web/auth/me')).toHaveLength(1));
    expect(bff.calls('POST /api/web/auth/refresh')).toHaveLength(1);
    expect(bff.calls('GET /api/web/auth/me')[0].headers.Authorization).toBe('Bearer restored');
    expect(auth.user?.userName).toBe('admin.prueba');
    expect(getAccessToken()).toBe('restored');
    expectNoStorageWrites();
  });

  it('sin cookie de refresh queda unauthenticated sin aviso de sesión caducada', async () => {
    createFakeBff({ 'POST /api/web/auth/refresh': () => problem(401, 'Authentication failed.') });
    renderAuth();
    await waitFor(() => expect(auth.status).toBe('unauthenticated'));
    expect(auth.signOutReason).toBeNull();
    expect(getAccessToken()).toBeNull();
  });

  it('login sin MFA: la sesión empieza de inmediato y los permisos son string[] del backend', async () => {
    const bff = createFakeBff({
      'POST /api/web/auth/refresh': () => problem(401, 'Authentication failed.'),
      'POST /api/web/auth/login': () => json(200, { mfaRequired: false, session: session(NURSE_USER, 'nurse-token'), mfaChallenge: null }),
    });
    renderAuth();
    await waitFor(() => expect(auth.status).toBe('unauthenticated'));

    let result!: Awaited<ReturnType<AuthContextValue['login']>>;
    await act(async () => {
      result = await auth.login('enfermeria.prueba', 'secreta');
    });
    expect(result.mfaRequired).toBe(false);
    expect(auth.status).toBe('authenticated');
    expect(auth.permissions).toEqual(['Patients.Read', 'Encounters.Read', 'Triage.Write']);
    expect(auth.can('Triage.Write')).toBe(true);
    expect(auth.can('Users.Manage')).toBe(false);
    expect(auth.hasRole('Nurse')).toBe(true);
    expect(getAccessToken()).toBe('nurse-token');
    expect(bff.calls('POST /api/web/auth/login')[0].body).toEqual({ identifier: 'enfermeria.prueba', password: 'secreta' });
    expectNoStorageWrites();
  });

  it('login con MFA: guarda el desafío solo en memoria y completeMfa con código TOTP crea la sesión', async () => {
    const bff = createFakeBff({
      'POST /api/web/auth/refresh': () => problem(401, 'Authentication failed.'),
      'POST /api/web/auth/login': () =>
        json(200, { mfaRequired: true, session: null, mfaChallenge: { token: 'challenge-1', expiresAtUtc: '2030-01-01T00:00:00Z' } }),
      'POST /api/web/auth/mfa/verify': () => json(200, session(ADMIN_USER, 'after-mfa')),
    });
    renderAuth();
    await waitFor(() => expect(auth.status).toBe('unauthenticated'));

    await act(async () => {
      const r = await auth.login('admin.prueba', 'secreta');
      expect(r.mfaRequired).toBe(true);
    });
    expect(auth.status).toBe('unauthenticated');
    expect(auth.mfaChallenge?.token).toBe('challenge-1');
    expect(getAccessToken()).toBeNull();

    await act(async () => {
      await auth.completeMfa({ code: '123456' });
    });
    expect(bff.calls('POST /api/web/auth/mfa/verify')[0].body).toEqual({ challengeToken: 'challenge-1', code: '123456' });
    expect(auth.status).toBe('authenticated');
    expect(auth.mfaChallenge).toBeNull();
    expect(getAccessToken()).toBe('after-mfa');
    expectNoStorageWrites();
  });

  it('MFA fallido (401): no reintenta solo, mantiene el desafío para un nuevo intento del usuario', async () => {
    const bff = createFakeBff({
      'POST /api/web/auth/refresh': () => problem(401, 'Authentication failed.'),
      'POST /api/web/auth/login': () =>
        json(200, { mfaRequired: true, session: null, mfaChallenge: { token: 'challenge-2', expiresAtUtc: '2030-01-01T00:00:00Z' } }),
      'POST /api/web/auth/mfa/verify': () => problem(401, 'Authentication failed.'),
    });
    renderAuth();
    await waitFor(() => expect(auth.status).toBe('unauthenticated'));
    await act(async () => {
      await auth.login('admin.prueba', 'secreta');
    });

    let error: unknown;
    await act(async () => {
      error = await auth.completeMfa({ recoveryCode: 'abcde-12345' }).catch((e: unknown) => e);
    });
    expect(isApiError(error, 401)).toBe(true);
    expect(bff.calls('POST /api/web/auth/mfa/verify')).toHaveLength(1);
    expect(bff.calls('POST /api/web/auth/mfa/verify')[0].body).toEqual({ challengeToken: 'challenge-2', recoveryCode: 'abcde-12345' });
    expect(auth.status).toBe('unauthenticated');
    expect(getAccessToken()).toBeNull();
  });

  it('login con credenciales inválidas (401) rechaza y no crea sesión', async () => {
    createFakeBff({
      'POST /api/web/auth/refresh': () => problem(401, 'Authentication failed.'),
      'POST /api/web/auth/login': () => problem(401, 'Authentication failed.'),
    });
    renderAuth();
    await waitFor(() => expect(auth.status).toBe('unauthenticated'));
    let error: unknown;
    await act(async () => {
      error = await auth.login('nadie', 'mal').catch((e: unknown) => e);
    });
    expect(isApiError(error, 401)).toBe(true);
    expect(auth.status).toBe('unauthenticated');
    expect(auth.mfaChallenge).toBeNull();
  });

  it('si el refresh automático falla, la sesión termina (unauthenticated, motivo expired)', async () => {
    createFakeBff({
      'POST /api/web/auth/refresh': (_req, n) => (n === 0 ? json(200, session(ADMIN_USER, 'first')) : problem(401, 'Authentication failed.')),
      'GET /api/web/auth/me': () => json(200, ADMIN_USER),
      'GET /api/web/admin/users': () => problem(401, 'Unauthorized'),
    });
    renderAuth();
    await waitFor(() => expect(auth.status).toBe('authenticated'));

    let error: unknown;
    await act(async () => {
      error = await apiRequest('/api/web/admin/users').catch((e: unknown) => e);
    });
    expect(isApiError(error, 401)).toBe(true);
    expect(auth.status).toBe('unauthenticated');
    expect(auth.signOutReason).toBe('expired');
    expect(getAccessToken()).toBeNull();
  });

  it('un 403 de la API no cierra la sesión', async () => {
    createFakeBff({
      'POST /api/web/auth/refresh': () => json(200, session(NURSE_USER, 'nurse')),
      'GET /api/web/auth/me': () => json(200, NURSE_USER),
      'GET /api/web/admin/users': () => problem(403, 'Forbidden'),
    });
    renderAuth();
    await waitFor(() => expect(auth.status).toBe('authenticated'));
    await act(async () => {
      await apiRequest('/api/web/admin/users').catch(() => undefined);
    });
    expect(auth.status).toBe('authenticated');
    expect(getAccessToken()).toBe('nurse');
  });

  it('logout llama al backend con CSRF y limpia la memoria aunque el backend falle', async () => {
    const bff = createFakeBff({
      'POST /api/web/auth/refresh': () => json(200, session(ADMIN_USER, 'tok')),
      'GET /api/web/auth/me': () => json(200, ADMIN_USER),
      'POST /api/web/auth/logout': () => problem(500, 'Boom'),
    });
    renderAuth();
    await waitFor(() => expect(auth.status).toBe('authenticated'));
    await act(async () => {
      await auth.logout();
    });
    const [req] = bff.calls('POST /api/web/auth/logout');
    expect(req.headers['X-SCIPH-CSRF']).toBe('1');
    expect(req.credentials).toBe('include');
    expect(auth.status).toBe('unauthenticated');
    expect(auth.user).toBeNull();
    expect(auth.permissions).toEqual([]);
    expect(getAccessToken()).toBeNull();
  });

  it('cambiar la contraseña cierra la sesión local (el backend revoca las sesiones)', async () => {
    const bff = createFakeBff({
      'POST /api/web/auth/refresh': () => json(200, session(ADMIN_USER, 'tok')),
      'GET /api/web/auth/me': () => json(200, ADMIN_USER),
      'POST /api/web/auth/change-password': noContent,
    });
    renderAuth();
    await waitFor(() => expect(auth.status).toBe('authenticated'));
    await act(async () => {
      await auth.changePassword('Actual#2026x', 'Nueva#2026xy');
    });
    expect(bff.calls('POST /api/web/auth/change-password')[0].body).toEqual({ currentPassword: 'Actual#2026x', newPassword: 'Nueva#2026xy' });
    expect(bff.calls('POST /api/web/auth/change-password')[0].headers.Authorization).toBe('Bearer tok');
    expect(auth.status).toBe('unauthenticated');
    expect(auth.signOutReason).toBe('password-changed');
  });
});
