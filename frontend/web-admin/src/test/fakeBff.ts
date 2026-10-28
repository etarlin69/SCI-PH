import { vi } from 'vitest';
import type { WebSession, WebUser } from '../auth';

/** Frontera HTTP simulada: los tests verifican el comportamiento del frontend contra los contratos reales del BFF. */
type FetchInput = Parameters<typeof fetch>[0];
type FetchInit = NonNullable<Parameters<typeof fetch>[1]>;

export interface RecordedRequest {
  method: string;
  path: string;
  headers: Record<string, string>;
  body: unknown;
  credentials: FetchInit['credentials'];
}

type Handler = (req: RecordedRequest, callIndex: number) => Response | Promise<Response>;

export function json(status: number, body?: unknown, headers: Record<string, string> = {}): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json', ...headers },
  });
}

export const problem = (status: number, title: string, extra: Record<string, unknown> = {}) => json(status, { status, title, ...extra });

export function createFakeBff(routes: Record<string, Handler>) {
  const requests: RecordedRequest[] = [];
  const counts = new Map<string, number>();
  const fetchMock = vi.fn(async (input: FetchInput, init: FetchInit = {}) => {
    const url = new URL(typeof input === 'string' ? input : input.toString(), 'http://localhost');
    const method = (init.method ?? 'GET').toUpperCase();
    const headers = Object.fromEntries(Object.entries((init.headers as Record<string, string>) ?? {}));
    const req: RecordedRequest = {
      method,
      path: url.pathname,
      headers,
      body: typeof init.body === 'string' ? JSON.parse(init.body) : init.body instanceof FormData ? init.body : undefined,
      credentials: init.credentials,
    };
    requests.push(req);
    const key = `${method} ${url.pathname}`;
    const n = counts.get(key) ?? 0;
    counts.set(key, n + 1);
    const handler = routes[key];
    if (!handler) return problem(404, `No route for ${key}`);
    return handler(req, n);
  });
  vi.stubGlobal('fetch', fetchMock);
  return {
    fetchMock,
    requests,
    calls: (key: string) => requests.filter((r) => `${r.method} ${r.path}` === key),
  };
}

export const ADMIN_USER: WebUser = {
  id: '11111111-1111-1111-1111-111111111111',
  userName: 'admin.prueba',
  email: 'admin.prueba@sci-ph.test',
  roles: ['Administrator'],
  permissions: ['Users.Manage', 'Accounts.InvitePatient', 'Devices.Manage', 'Audit.Read'],
};

export const NURSE_USER: WebUser = {
  id: '22222222-2222-2222-2222-222222222222',
  userName: 'enfermeria.prueba',
  email: null,
  roles: ['Nurse'],
  permissions: ['Patients.Read', 'Encounters.Read', 'Triage.Write'],
};

export function session(user: WebUser = ADMIN_USER, token = 'access-1'): WebSession {
  return { accessToken: token, accessTokenExpiresAtUtc: '2030-01-01T00:00:00Z', user };
}
