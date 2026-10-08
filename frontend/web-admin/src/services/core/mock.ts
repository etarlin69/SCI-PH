import { ServiceError } from './errors';

export type MockScenario = 'normal' | 'slow' | 'error' | 'empty' | 'failwrite';

export function getMockScenario(): MockScenario {
  if (typeof window === 'undefined') return 'normal';
  const value = new URLSearchParams(window.location.search).get('mock');
  return value === 'slow' || value === 'error' || value === 'empty' || value === 'failwrite' ? value : 'normal';
}

const BASE_LATENCY_MS = 450;

function latency(): number {
  return getMockScenario() === 'slow' ? 2500 : BASE_LATENCY_MS + Math.floor(Math.random() * 250);
}

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

/** Copia profunda para que la UI nunca mute el "estado del servidor" por referencia. */
export function clone<T>(value: T): T {
  return typeof structuredClone === 'function' ? structuredClone(value) : (JSON.parse(JSON.stringify(value)) as T);
}

/** Simula un GET. */
export async function mockRead<T>(produce: () => T): Promise<T> {
  await sleep(latency());
  if (getMockScenario() === 'error') {
    throw new ServiceError('SERVER', 'El servicio no respondió (error 503). Inténtalo de nuevo en unos segundos.');
  }
  return clone(produce());
}

/** Simula un GET de colección (respeta además el escenario `empty`). */
export async function mockList<T>(produce: () => T[]): Promise<T[]> {
  return mockRead(() => (getMockScenario() === 'empty' ? [] : produce()));
}

const failedOnce = new Set<string>();

/** Simula un POST/PUT/DELETE. */
export async function mockWrite<T>(operation: string, perform: () => T): Promise<T> {
  await sleep(latency() + 300);
  if (getMockScenario() === 'failwrite' && !failedOnce.has(operation)) {
    failedOnce.add(operation);
    throw new ServiceError('SERVER', 'El servidor no respondió (503). Los datos no se guardaron; vuelve a intentarlo.');
  }
  return clone(perform());
}

/** `PREFIX-0001` siguiente a partir de los ids existentes. */
export function nextId(prefix: string, existing: string[], width = 3): string {
  const max = existing.reduce((m, id) => {
    const n = Number(id.replace(/\D/g, ''));
    return id.startsWith(prefix) && Number.isFinite(n) ? Math.max(m, n) : m;
  }, 0);
  return `${prefix}${String(max + 1).padStart(width, '0')}`;
}

const pad = (n: number) => String(n).padStart(2, '0');

/** `YYYY-MM-DD HH:mm` local. */
export function nowDateTime(d = new Date()): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** `YYYY-MM-DD HH:mm:ss` local. */
export function nowTimestamp(d = new Date()): string {
  return `${nowDateTime(d)}:${pad(d.getSeconds())}`;
}

export function notFound(what: string): never {
  throw new ServiceError('NOT_FOUND', `${what} no existe o fue eliminado.`);
}
