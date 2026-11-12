import { describe, expect, it, vi } from 'vitest';
import { authApi } from '../../auth/authApi';
import { createFakeBff, json, problem } from '../../test/fakeBff';
import { ApiError, apiRequest, describeApiError, setAccessToken, setRefreshHandler } from '..';

describe('apiClient', () => {
  it('envía FormData tal cual, sin Content-Type JSON (el navegador pone el boundary del multipart)', async () => {
    const bff = createFakeBff({ 'POST /api/web/patients/p-1/faces/verify': () => json(200, { verificationId: 'v', outcome: 'Match' }) });
    setAccessToken('tok-123');
    const form = new FormData();
    form.append('clientRequestId', 'k-1');
    await apiRequest('/api/web/patients/p-1/faces/verify', { method: 'POST', body: form });
    const [req] = bff.calls('POST /api/web/patients/p-1/faces/verify');
    expect(req.headers['Content-Type']).toBeUndefined();
    expect(req.body).toBe(form);
    expect(req.headers.Authorization).toBe('Bearer tok-123');
  });

  it('adjunta el Bearer en memoria y credentials include', async () => {
    const bff = createFakeBff({ 'GET /api/web/admin/users': () => json(200, { items: [], page: 1, pageSize: 20, total: 0 }) });
    setAccessToken('tok-123');
    await apiRequest('/api/web/admin/users');
    const [req] = bff.calls('GET /api/web/admin/users');
    expect(req.headers.Authorization).toBe('Bearer tok-123');
    expect(req.credentials).toBe('include');
    expect(req.headers['X-SCIPH-CSRF']).toBeUndefined();
  });

  it('envía X-SCIPH-CSRF: 1 y no envía Bearer en login, mfa/verify, refresh y logout', async () => {
    const ok = () => json(200, { mfaRequired: false, session: null, mfaChallenge: null });
    const bff = createFakeBff({
      'POST /api/web/auth/login': ok,
      'POST /api/web/auth/mfa/verify': ok,
      'POST /api/web/auth/refresh': ok,
      'POST /api/web/auth/logout': () => new Response(null, { status: 204 }),
    });
    setAccessToken('tok-123');
    await authApi.login({ identifier: 'a', password: 'b' });
    await authApi.verifyMfa({ challengeToken: 'c', code: '123456' });
    await authApi.refresh();
    await authApi.logout();
    expect(bff.requests).toHaveLength(4);
    for (const req of bff.requests) {
      expect(req.headers['X-SCIPH-CSRF']).toBe('1');
      expect(req.headers.Authorization).toBeUndefined();
      expect(req.credentials).toBe('include');
    }
    expect(bff.requests[0].body).toEqual({ identifier: 'a', password: 'b' });
  });

  it('single-flight: 5 peticiones con 401 producen un único refresh y cada una se reintenta una vez', async () => {
    let currentToken = 'old';
    const bff = createFakeBff({
      'GET /api/web/admin/devices': (req) => (req.headers.Authorization === 'Bearer new' ? json(200, []) : problem(401, 'Unauthorized')),
    });
    setAccessToken(currentToken);
    let release!: () => void;
    const gate = new Promise<void>((r) => (release = r));
    const handler = vi.fn(async () => {
      await gate;
      currentToken = 'new';
      setAccessToken(currentToken);
      return currentToken;
    });
    setRefreshHandler(handler);

    const pending = Array.from({ length: 5 }, () => apiRequest('/api/web/admin/devices'));
    await vi.waitFor(() => expect(bff.calls('GET /api/web/admin/devices')).toHaveLength(5));
    release();
    await expect(Promise.all(pending)).resolves.toHaveLength(5);

    expect(handler).toHaveBeenCalledTimes(1);
    expect(bff.calls('GET /api/web/admin/devices')).toHaveLength(10);
  });

  it('reintenta una sola vez: si tras el refresh vuelve el 401 lo propaga sin bucles', async () => {
    const bff = createFakeBff({ 'GET /api/web/auth/me': () => problem(401, 'Authentication failed.') });
    setAccessToken('old');
    const handler = vi.fn(async () => {
      setAccessToken('new');
      return 'new';
    });
    setRefreshHandler(handler);
    await expect(apiRequest('/api/web/auth/me')).rejects.toMatchObject({ status: 401 });
    expect(handler).toHaveBeenCalledTimes(1);
    expect(bff.calls('GET /api/web/auth/me')).toHaveLength(2);
  });

  it('un 403 no dispara refresh ni reintento', async () => {
    const bff = createFakeBff({ 'GET /api/web/admin/users': () => problem(403, 'Forbidden') });
    setAccessToken('tok');
    const handler = vi.fn(async () => 'other');
    setRefreshHandler(handler);
    const error = await apiRequest('/api/web/admin/users').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(403);
    expect(handler).not.toHaveBeenCalled();
    expect(bff.requests).toHaveLength(1);
  });

  it('un 401 sin sesión (sin token) no intenta refrescar', async () => {
    createFakeBff({ 'POST /api/web/auth/login': () => problem(401, 'Authentication failed.') });
    const handler = vi.fn(async () => 'x');
    setRefreshHandler(handler);
    await expect(authApi.login({ identifier: 'a', password: 'b' })).rejects.toMatchObject({ status: 401 });
    expect(handler).not.toHaveBeenCalled();
  });

  it('interpreta ProblemDetails, errores de validación y Retry-After', async () => {
    createFakeBff({
      'POST /api/web/auth/reset-password': () =>
        json(400, { title: 'One or more validation errors occurred.', status: 400, errors: { NewPassword: ['Passwords must have at least one digit.'] } }),
      'POST /api/web/auth/forgot-password': () => problem(429, 'Too many requests.', {}),
    });
    const validation = (await authApi.resetPassword({ userId: 'u', token: 't', newPassword: 'x' }).catch((e: unknown) => e)) as ApiError;
    expect(validation.status).toBe(400);
    expect(validation.fieldErrors).toEqual({ newPassword: ['Passwords must have at least one digit.'] });

    vi.stubGlobal('fetch', vi.fn(async () => json(429, { status: 429, title: 'Too many requests.' }, { 'Retry-After': '120' })));
    const limited = (await authApi.forgotPassword('a@b.c').catch((e: unknown) => e)) as ApiError;
    expect(limited.status).toBe(429);
    expect(limited.retryAfterSeconds).toBe(120);
    expect(describeApiError(limited)).toContain('2 min');
  });
});
