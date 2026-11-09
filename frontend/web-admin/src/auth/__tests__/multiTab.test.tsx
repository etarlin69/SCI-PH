import { StrictMode } from 'react';
import { act, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { REFRESH_LOCK_NAME, apiRequest, getAccessToken } from '../../api';
import { ADMIN_USER, NURSE_USER, createFakeBff, json, problem, session } from '../../test/fakeBff';
import { AuthProvider, useAuth, type AuthContextValue } from '../AuthContext';
import { AUTH_CHANNEL_NAME, parseAuthChannelEvent } from '../authChannel';
import { REFRESH_SUPERSEDED_CODE } from '../refreshFlow';

/* ---------- Dobles del navegador: Web Locks y BroadcastChannel compartidos "entre pestañas" ---------- */

interface FakeLocks {
  request: ReturnType<typeof vi.fn>;
  requests: { name: string; mode: string | undefined }[];
  readonly held: boolean;
}

/** LockManager exclusivo real en cola: una sola tarea a la vez por nombre, como el del navegador. */
function installFakeLocks(): FakeLocks {
  let tail: Promise<unknown> = Promise.resolve();
  let held = false;
  const requests: FakeLocks['requests'] = [];
  const request = vi.fn((name: string, options: { mode?: string }, callback: (lock: { name: string; mode: string }) => Promise<unknown>) => {
    requests.push({ name, mode: options?.mode });
    const run = tail.then(async () => {
      held = true;
      try {
        return await callback({ name, mode: options?.mode ?? 'exclusive' });
      } finally {
        held = false;
      }
    });
    tail = run.catch(() => undefined);
    return run;
  });
  const fake: FakeLocks = {
    request,
    requests,
    get held() {
      return held;
    },
  };
  Object.defineProperty(navigator, 'locks', { value: { request }, configurable: true });
  return fake;
}

function removeLocks() {
  Object.defineProperty(navigator, 'locks', { value: undefined, configurable: true });
}

/** Bus en memoria con la semántica de BroadcastChannel: el emisor no recibe su propio mensaje. */
class FakeBroadcastChannel {
  static channels = new Set<FakeBroadcastChannel>();
  static posted: { name: string; data: unknown }[] = [];
  onmessage: ((event: MessageEvent) => void) | null = null;
  closed = false;

  constructor(readonly name: string) {
    FakeBroadcastChannel.channels.add(this);
  }

  postMessage(data: unknown) {
    FakeBroadcastChannel.posted.push({ name: this.name, data });
    for (const peer of FakeBroadcastChannel.channels) {
      if (peer === this || peer.closed || peer.name !== this.name) continue;
      const copy = structuredClone(data);
      queueMicrotask(() => peer.onmessage?.({ data: copy } as MessageEvent));
    }
  }

  close() {
    this.closed = true;
    FakeBroadcastChannel.channels.delete(this);
  }
}

/** Otra pestaña del mismo navegador: solo habla por el canal. */
function otherTab() {
  const channel = new FakeBroadcastChannel(AUTH_CHANNEL_NAME);
  const received: unknown[] = [];
  channel.onmessage = (event) => received.push(event.data);
  return { channel, received, post: (data: unknown) => channel.postMessage(data) };
}

/* ---------- Render ---------- */

let auth!: AuthContextValue;
function Probe() {
  auth = useAuth();
  return null;
}

function renderAuth(props: { startupRetryDelaysMs?: number[]; peerRefreshWaitMs?: number } = {}) {
  return render(
    <StrictMode>
      <AuthProvider startupRetryDelaysMs={props.startupRetryDelaysMs ?? [0, 0]} peerRefreshWaitMs={props.peerRefreshWaitMs ?? 0}>
        <Probe />
      </AuthProvider>
    </StrictMode>,
  );
}

const superseded = () => problem(409, 'The session was refreshed by a concurrent request.', { code: REFRESH_SUPERSEDED_CODE });
const unauthorized = () => problem(401, 'Authentication failed.');
const refreshed = (token: string, user = ADMIN_USER) => json(200, session(user, token));
const me = () => json(200, ADMIN_USER);

const REFRESH = 'POST /api/web/auth/refresh';

describe('sesión web con varias pestañas', () => {
  let storageWrites: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    FakeBroadcastChannel.channels.clear();
    FakeBroadcastChannel.posted = [];
    vi.stubGlobal('BroadcastChannel', FakeBroadcastChannel);
    storageWrites = vi.spyOn(Storage.prototype, 'setItem');
    removeLocks();
  });

  afterEach(() => {
    removeLocks();
    vi.unstubAllGlobals();
    expect(storageWrites).not.toHaveBeenCalled();
    expect(document.cookie).toBe('');
  });

  describe('Web Locks', () => {
    it('con navigator.locks, el refresh se ejecuta dentro del lock exclusivo sciph-web-refresh', async () => {
      const locks = installFakeLocks();
      const heldDuringRefresh: boolean[] = [];
      createFakeBff({
        [REFRESH]: () => {
          heldDuringRefresh.push(locks.held);
          return refreshed('restored');
        },
        'GET /api/web/auth/me': me,
      });

      renderAuth();
      await waitFor(() => expect(auth.status).toBe('authenticated'));

      expect(heldDuringRefresh).toEqual([true]);
      expect(locks.requests).toEqual([{ name: REFRESH_LOCK_NAME, mode: 'exclusive' }]);
      expect(REFRESH_LOCK_NAME).toBe('sciph-web-refresh');
    });

    it('espera a que otra pestaña suelte el lock antes de enviar su refresh (serialización entre pestañas)', async () => {
      const locks = installFakeLocks();
      const bff = createFakeBff({ [REFRESH]: () => refreshed('after-peer'), 'GET /api/web/auth/me': me });
      let releasePeer!: () => void;
      // La otra pestaña tiene el lock (su refresh está en curso).
      void locks.request(REFRESH_LOCK_NAME, { mode: 'exclusive' }, () => new Promise<void>((r) => (releasePeer = r)));

      renderAuth();
      await new Promise((r) => setTimeout(r, 20));
      expect(bff.calls(REFRESH)).toHaveLength(0);
      expect(auth.status).toBe('initializing');

      releasePeer();
      await waitFor(() => expect(auth.status).toBe('authenticated'));
      expect(bff.calls(REFRESH)).toHaveLength(1);
      expect(getAccessToken()).toBe('after-peer');
    });

    it('sin navigator.locks funciona como antes (una pestaña, single-flight)', async () => {
      expect((navigator as Navigator & { locks?: unknown }).locks).toBeUndefined();
      const bff = createFakeBff({ [REFRESH]: () => refreshed('restored'), 'GET /api/web/auth/me': me });

      renderAuth();
      await waitFor(() => expect(auth.status).toBe('authenticated'));
      expect(bff.calls(REFRESH)).toHaveLength(1);
    });

    it('si el lock no llega a tiempo (AbortError antes de obtenerlo) renueva sin él, una sola vez', async () => {
      const request = vi.fn(async () => {
        throw new DOMException('Timed out', 'AbortError');
      });
      Object.defineProperty(navigator, 'locks', { value: { request }, configurable: true });
      const bff = createFakeBff({ [REFRESH]: () => refreshed('unlocked'), 'GET /api/web/auth/me': me });

      renderAuth();
      await waitFor(() => expect(auth.status).toBe('authenticated'));
      expect(bff.calls(REFRESH)).toHaveLength(1);
    });
  });

  describe('409 refresh-superseded', () => {
    it('reintenta EXACTAMENTE una vez y la sesión continúa', async () => {
      const bff = createFakeBff({
        [REFRESH]: (_req, n) => (n === 0 ? superseded() : refreshed('winner-cookie')),
        'GET /api/web/auth/me': me,
      });

      renderAuth();
      await waitFor(() => expect(auth.status).toBe('authenticated'));
      expect(bff.calls(REFRESH)).toHaveLength(2);
      expect(getAccessToken()).toBe('winner-cookie');
    });

    it('el reintento espera al session-refreshed de la pestaña ganadora (la cookie nueva ya está guardada)', async () => {
      const peer = otherTab();
      const bff = createFakeBff({
        [REFRESH]: (_req, n) => (n === 0 ? superseded() : refreshed('winner-cookie')),
        'GET /api/web/auth/me': me,
      });

      renderAuth({ peerRefreshWaitMs: 60_000 });
      await waitFor(() => expect(bff.calls(REFRESH)).toHaveLength(1));
      await new Promise((r) => setTimeout(r, 20));
      expect(bff.calls(REFRESH)).toHaveLength(1);

      act(() => peer.post({ type: 'session-refreshed' }));
      await waitFor(() => expect(auth.status).toBe('authenticated'));
      expect(bff.calls(REFRESH)).toHaveLength(2);
    });

    it('un segundo 409 no se reintenta: sin bucle y sin cerrar la sesión (queda reintentable)', async () => {
      const bff = createFakeBff({ [REFRESH]: superseded });

      renderAuth({ startupRetryDelaysMs: [] });
      await waitFor(() => expect(auth.status).toBe('unavailable'));
      expect(bff.calls(REFRESH)).toHaveLength(2);
      await new Promise((r) => setTimeout(r, 30));
      expect(bff.calls(REFRESH)).toHaveLength(2);
    });

    it('a mitad de sesión, un 409 doble no cierra la sesión ni reintenta más de una vez', async () => {
      const bff = createFakeBff({
        [REFRESH]: (_req, n) => (n === 0 ? refreshed('first') : superseded()),
        'GET /api/web/auth/me': me,
        'GET /api/web/admin/users': unauthorized,
      });
      renderAuth();
      await waitFor(() => expect(auth.status).toBe('authenticated'));

      await act(async () => {
        await expect(apiRequest('/api/web/admin/users')).rejects.toMatchObject({ status: 401 });
      });
      expect(bff.calls(REFRESH)).toHaveLength(3); // arranque + 409 + un único reintento
      expect(auth.status).toBe('authenticated');
    });

    it('un 409 que no es refresh-superseded no se reintenta', async () => {
      const bff = createFakeBff({ [REFRESH]: () => problem(409, 'Conflict', { code: 'something-else' }) });

      renderAuth({ startupRetryDelaysMs: [] });
      await waitFor(() => expect(auth.status).toBe('unavailable'));
      expect(bff.calls(REFRESH)).toHaveLength(1);
    });
  });

  describe('fallos del refresh', () => {
    it('401 a mitad de sesión: cierra la sesión como caducada, un solo refresh, sin bucle, y avisa a las demás pestañas', async () => {
      const peer = otherTab();
      const bff = createFakeBff({
        [REFRESH]: (_req, n) => (n === 0 ? refreshed('first') : unauthorized()),
        'GET /api/web/auth/me': me,
        'GET /api/web/admin/users': unauthorized,
      });
      renderAuth();
      await waitFor(() => expect(auth.status).toBe('authenticated'));

      await act(async () => {
        await expect(apiRequest('/api/web/admin/users')).rejects.toMatchObject({ status: 401 });
      });

      expect(auth.status).toBe('unauthenticated');
      expect(auth.signOutReason).toBe('expired');
      expect(getAccessToken()).toBeNull();
      expect(bff.calls(REFRESH)).toHaveLength(2);
      expect(bff.calls('GET /api/web/admin/users')).toHaveLength(1);
      await waitFor(() => expect(peer.received).toContainEqual({ type: 'session-ended', reason: 'expired' }));
    });

    it('429 al arrancar no se trata como sesión cerrada: reintenta y recupera la sesión', async () => {
      const bff = createFakeBff({
        [REFRESH]: (_req, n) => (n === 0 ? problem(429, 'Too many requests.') : refreshed('later')),
        'GET /api/web/auth/me': me,
      });

      renderAuth({ startupRetryDelaysMs: [0] });
      await waitFor(() => expect(auth.status).toBe('authenticated'));
      expect(bff.calls(REFRESH)).toHaveLength(2);
    });

    it('429 persistente al arrancar: intentos acotados y estado unavailable (no unauthenticated)', async () => {
      const bff = createFakeBff({ [REFRESH]: () => problem(429, 'Too many requests.') });

      renderAuth({ startupRetryDelaysMs: [0, 0] });
      await waitFor(() => expect(auth.status).toBe('unavailable'));
      expect(auth.signOutReason).toBeNull();
      expect(bff.calls(REFRESH)).toHaveLength(3);
      await new Promise((r) => setTimeout(r, 30));
      expect(bff.calls(REFRESH)).toHaveLength(3);
    });

    it('fallo de red al arrancar: unavailable; «Reintentar» recupera la sesión', async () => {
      let online = false;
      const bff = createFakeBff({
        [REFRESH]: () => {
          if (!online) throw new TypeError('Failed to fetch');
          return refreshed('back-online');
        },
        'GET /api/web/auth/me': me,
      });

      renderAuth({ startupRetryDelaysMs: [0] });
      await waitFor(() => expect(auth.status).toBe('unavailable'));
      expect(bff.calls(REFRESH)).toHaveLength(2);

      online = true;
      await act(async () => {
        await auth.retrySession();
      });
      expect(auth.status).toBe('authenticated');
      expect(getAccessToken()).toBe('back-online');
    });

    it('401 al arrancar sigue siendo "sin sesión" (sin aviso y sin reintentos)', async () => {
      const bff = createFakeBff({ [REFRESH]: unauthorized });

      renderAuth({ startupRetryDelaysMs: [0, 0] });
      await waitFor(() => expect(auth.status).toBe('unauthenticated'));
      expect(auth.signOutReason).toBeNull();
      expect(bff.calls(REFRESH)).toHaveLength(1);
    });
  });

  describe('BroadcastChannel sciph-auth', () => {
    async function authenticated() {
      const bff = createFakeBff({
        [REFRESH]: () => refreshed('tab-token'),
        'GET /api/web/auth/me': me,
        'POST /api/web/auth/logout': () => new Response(null, { status: 204 }),
      });
      renderAuth();
      await waitFor(() => expect(auth.status).toBe('authenticated'));
      return bff;
    }

    it('logged-out de otra pestaña: limpia el estado local, sin llamar a logout ni al backend', async () => {
      const peer = otherTab();
      const bff = await authenticated();
      const before = bff.requests.length;

      act(() => peer.post({ type: 'logged-out' }));

      await waitFor(() => expect(auth.status).toBe('unauthenticated'));
      expect(auth.signOutReason).toBe('logout');
      expect(getAccessToken()).toBeNull();
      expect(bff.requests).toHaveLength(before);
      // Quien recibe no reenvía: sin bucles.
      expect(FakeBroadcastChannel.posted.filter((m) => (m.data as { type: string }).type === 'logged-out')).toHaveLength(1);
    });

    it('session-ended de otra pestaña: limpia el estado con su motivo', async () => {
      const peer = otherTab();
      await authenticated();

      act(() => peer.post({ type: 'session-ended', reason: 'password-changed' }));

      await waitFor(() => expect(auth.status).toBe('unauthenticated'));
      expect(auth.signOutReason).toBe('password-changed');
    });

    it('session-refreshed en una pestaña autenticada no hace nada (ni peticiones ni cambios)', async () => {
      const peer = otherTab();
      const bff = await authenticated();
      const before = bff.requests.length;

      act(() => peer.post({ type: 'session-refreshed' }));
      await new Promise((r) => setTimeout(r, 20));

      expect(auth.status).toBe('authenticated');
      expect(getAccessToken()).toBe('tab-token');
      expect(bff.requests).toHaveLength(before);
    });

    it('session-refreshed en una pestaña unavailable vuelve a comprobar la sesión', async () => {
      const peer = otherTab();
      let limited = true;
      createFakeBff({
        [REFRESH]: () => (limited ? problem(429, 'Too many requests.') : refreshed('from-peer-cookie')),
        'GET /api/web/auth/me': me,
      });
      renderAuth({ startupRetryDelaysMs: [] });
      await waitFor(() => expect(auth.status).toBe('unavailable'));

      limited = false;
      act(() => peer.post({ type: 'session-refreshed' }));

      await waitFor(() => expect(auth.status).toBe('authenticated'));
      expect(getAccessToken()).toBe('from-peer-cookie');
    });

    it('descarta mensajes desconocidos o mal formados', async () => {
      const peer = otherTab();
      await authenticated();

      act(() => {
        peer.post({ type: 'session-ended', reason: 'hacked' });
        peer.post({ type: 'logout-now' });
        peer.post('logged-out');
        peer.post(null);
      });
      await new Promise((r) => setTimeout(r, 20));

      expect(auth.status).toBe('authenticated');
      expect(parseAuthChannelEvent({ type: 'logged-out', accessToken: 'x' })).toEqual({ type: 'logged-out' });
    });

    it('logout propaga logged-out; cambio de contraseña y fin por MFA propagan session-ended', async () => {
      const peer = otherTab();
      createFakeBff({
        [REFRESH]: () => refreshed('tab-token'),
        'GET /api/web/auth/me': me,
        'POST /api/web/auth/logout': () => new Response(null, { status: 204 }),
        'POST /api/web/auth/change-password': () => new Response(null, { status: 204 }),
        'POST /api/web/auth/login': () => json(200, { mfaRequired: false, session: session(NURSE_USER, 'login-token'), mfaChallenge: null }),
      });
      renderAuth();
      await waitFor(() => expect(auth.status).toBe('authenticated'));

      await act(async () => {
        await auth.logout();
      });
      await act(async () => {
        await auth.login('enfermeria.prueba', 'secreta');
      });
      await act(async () => {
        await auth.changePassword('old', 'new');
      });
      await act(async () => {
        await auth.login('enfermeria.prueba', 'secreta');
      });
      act(() => auth.endSession('mfa-changed'));

      await waitFor(() =>
        expect(peer.received).toEqual([
          { type: 'session-refreshed' }, // refresh del arranque
          { type: 'logged-out' },
          { type: 'session-refreshed' }, // login
          { type: 'session-ended', reason: 'password-changed' },
          { type: 'session-refreshed' },
          { type: 'session-ended', reason: 'mfa-changed' },
        ]),
      );
    });

    it('ningún mensaje del canal contiene credenciales ni datos del usuario', async () => {
      createFakeBff({
        [REFRESH]: (_req, n) => (n === 0 ? refreshed('secret-access-token') : unauthorized()),
        'GET /api/web/auth/me': me,
        'GET /api/web/admin/users': unauthorized,
        'POST /api/web/auth/logout': () => new Response(null, { status: 204 }),
        'POST /api/web/auth/login': () => json(200, { mfaRequired: false, session: session(ADMIN_USER, 'secret-login-token'), mfaChallenge: null }),
      });
      renderAuth();
      await waitFor(() => expect(auth.status).toBe('authenticated'));
      await act(async () => {
        await apiRequest('/api/web/admin/users').catch(() => undefined);
      });
      await act(async () => {
        await auth.login('admin.prueba', 'secreta');
      });
      await act(async () => {
        await auth.logout();
      });

      expect(FakeBroadcastChannel.posted.length).toBeGreaterThanOrEqual(4);
      for (const { name, data } of FakeBroadcastChannel.posted) {
        expect(name).toBe('sciph-auth');
        expect(Object.keys(data as object).every((k) => k === 'type' || k === 'reason')).toBe(true);
        const text = JSON.stringify(data);
        for (const secret of ['secret-access-token', 'secret-login-token', ADMIN_USER.id, ADMIN_USER.email!, 'Bearer']) {
          expect(text).not.toContain(secret);
        }
      }
    });

    it('sin BroadcastChannel la pestaña funciona sola', async () => {
      vi.stubGlobal('BroadcastChannel', undefined);
      await authenticated();
      await act(async () => {
        await auth.logout();
      });
      expect(auth.status).toBe('unauthenticated');
    });
  });
});
