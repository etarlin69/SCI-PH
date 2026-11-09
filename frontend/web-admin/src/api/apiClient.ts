import { ApiError, NetworkError, parseRetryAfter } from './apiError';
import { isProblemDetails, type ProblemDetails } from './problemDetails';

/**
 * Cliente HTTP único del frontend web (Admin y Hospital).
 *
 * - El access token vive solo en memoria de este módulo; nunca en localStorage/sessionStorage/cookies JS.
 * - El refresh token lo gestiona el navegador: cookie HttpOnly `__Host-sciph-refresh`, enviada con
 *   `credentials: 'include'`. JavaScript no puede leerla ni enviarla a mano.
 * - Ante un 401 en una petición autenticada se hace UN refresh compartido (single-flight) y se reintenta UNA vez.
 * - Un 403 nunca toca la sesión: se propaga como ApiError(403).
 */

/**
 * Origen del Web BFF para las peticiones del navegador. Vacío = mismo origen: en desarrollo Vite redirige `/api`
 * al BFF (vite.config.ts) y en despliegue un proxy inverso hace lo mismo. Así la cookie es first-party y
 * `Retry-After` es legible (el BFF no lo expone por CORS).
 */
export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '');

/** Cabecera que el BFF exige en login, mfa/verify, refresh y logout (CookieCsrfProtection). */
export const CSRF_HEADER = 'X-SCIPH-CSRF';

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  /** JSON, o FormData (multipart/form-data: el navegador pone el Content-Type con su boundary). */
  body?: unknown;
  query?: Record<string, string | number | undefined | null>;
  /** Adjunta el Bearer y permite el refresh automático ante 401. Por defecto true. */
  auth?: boolean;
  /** Añade `X-SCIPH-CSRF: 1`. Solo para los endpoints que leen o escriben la cookie de refresh. */
  csrf?: boolean;
  signal?: AbortSignal;
}

let accessToken: string | null = null;
let refreshHandler: (() => Promise<string | null>) | null = null;
let refreshInFlight: Promise<string | null> | null = null;

export function setAccessToken(token: string | null): void {
  accessToken = token;
}

export function getAccessToken(): string | null {
  return accessToken;
}

/** El AuthProvider registra aquí cómo renovar la sesión (devuelve el token nuevo o null si la sesión terminó). */
export function setRefreshHandler(handler: (() => Promise<string | null>) | null): void {
  refreshHandler = handler;
}

/**
 * Nombre del Web Lock que serializa los refresh entre pestañas del mismo perfil del navegador. Es una constante: no
 * contiene ningún dato del usuario ni de la sesión.
 */
export const REFRESH_LOCK_NAME = 'sciph-web-refresh';

/** Espera máxima por el lock. Si otra pestaña lo retiene más (congelada, colgada), se renueva sin él. */
export const REFRESH_LOCK_WAIT_MS = 10_000;

type LockCapableNavigator = Navigator & { locks?: LockManager };

/**
 * Ejecuta `task` con el lock exclusivo `sciph-web-refresh` si el navegador tiene Web Locks; si no, directamente
 * (comportamiento anterior, de una sola pestaña). Con el lock, cada pestaña envía la cookie vigente: la anterior ya
 * guardó la de su Set-Cookie antes de soltarlo, así que nunca se presentan dos veces el mismo refresh token.
 */
function runWithRefreshLock<T>(task: () => Promise<T>): Promise<T> {
  const locks = typeof navigator === 'undefined' ? undefined : (navigator as LockCapableNavigator).locks;
  if (!locks || typeof locks.request !== 'function') return task();

  let started = false;
  const signal = typeof AbortSignal.timeout === 'function' ? AbortSignal.timeout(REFRESH_LOCK_WAIT_MS) : undefined;
  const locked = locks.request(REFRESH_LOCK_NAME, { mode: 'exclusive', signal }, () => {
    started = true;
    return task();
  }) as Promise<T>;
  return locked.catch((error: unknown) => {
    // Solo si no se obtuvo el lock a tiempo; un error de la propia tarea se propaga tal cual (sin reintentos).
    if (!started && error instanceof DOMException && (error.name === 'AbortError' || error.name === 'TimeoutError')) return task();
    throw error;
  });
}

/**
 * Renueva el access token. Todas las llamadas concurrentes de esta pestaña comparten la misma promesa (un solo
 * POST /refresh) y, entre pestañas, el Web Lock las serializa.
 */
export function refreshAccessToken(): Promise<string | null> {
  if (!refreshHandler) return Promise.resolve(null);
  if (!refreshInFlight) {
    const handler = refreshHandler;
    refreshInFlight = runWithRefreshLock(handler).finally(() => {
      refreshInFlight = null;
    });
  }
  return refreshInFlight;
}

function buildUrl(path: string, query?: RequestOptions['query']): string {
  let url = `${API_BASE_URL}${path}`;
  if (query) {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') params.set(key, String(value));
    }
    const qs = params.toString();
    if (qs) url += `?${qs}`;
  }
  return url;
}

async function readBody(response: Response): Promise<unknown> {
  if (response.status === 204 || response.status === 205) return undefined;
  const text = await response.text();
  if (!text) return undefined;
  const type = response.headers.get('Content-Type') ?? '';
  if (type.includes('json')) {
    try {
      return JSON.parse(text);
    } catch {
      return undefined;
    }
  }
  return undefined;
}

async function send(path: string, options: RequestOptions, token: string | null): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json, application/problem+json' };
  const isForm = options.body instanceof FormData;
  if (options.body !== undefined && !isForm) headers['Content-Type'] = 'application/json';
  if (options.csrf) headers[CSRF_HEADER] = '1';
  if (options.auth !== false && token) headers.Authorization = `Bearer ${token}`;

  try {
    return await fetch(buildUrl(path, options.query), {
      method: options.method ?? 'GET',
      headers,
      body: isForm ? (options.body as FormData) : options.body !== undefined ? JSON.stringify(options.body) : undefined,
      credentials: 'include',
      cache: 'no-store',
      signal: options.signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new NetworkError(error);
  }
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const usesAuth = options.auth !== false;
  const tokenUsed = usesAuth ? accessToken : null;
  let response = await send(path, options, tokenUsed);

  // 401 de una petición que llevaba sesión: el access token caducó o fue revocado. Un refresh y un reintento.
  if (response.status === 401 && usesAuth && tokenUsed) {
    const renewed = await refreshAccessToken();
    if (renewed) response = await send(path, options, renewed);
  }

  const body = await readBody(response);
  if (!response.ok) {
    const problem: ProblemDetails | null = isProblemDetails(body) ? body : null;
    throw new ApiError(response.status, problem, parseRetryAfter(response.headers.get('Retry-After')));
  }
  return body as T;
}

/** Solo para tests: deja el módulo como recién cargado. */
export function resetApiClientForTests(): void {
  accessToken = null;
  refreshHandler = null;
  refreshInFlight = null;
}
