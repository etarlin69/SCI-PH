import { validationErrors, type ProblemDetails } from './problemDetails';

/** Respuesta HTTP no exitosa del Web BFF. `problem` es el ProblemDetails si el cuerpo lo traía. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | null;
  /** Segundos de `Retry-After` (429); null si el backend no lo envió. */
  readonly retryAfterSeconds: number | null;

  constructor(status: number, problem: ProblemDetails | null, retryAfterSeconds: number | null = null) {
    super(problem?.title ?? `HTTP ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
    this.retryAfterSeconds = retryAfterSeconds;
  }

  get fieldErrors(): Record<string, string[]> {
    return validationErrors(this.problem);
  }
}

/** El servidor no respondió (sin red, BFF caído, certificado no confiable…). */
export class NetworkError extends Error {
  constructor(cause?: unknown) {
    super('No se pudo conectar con el servidor.');
    this.name = 'NetworkError';
    if (cause !== undefined) (this as { cause?: unknown }).cause = cause;
  }
}

export const isApiError = (e: unknown, status?: number): e is ApiError =>
  e instanceof ApiError && (status === undefined || e.status === status);

/** Segundos de `Retry-After` (solo el formato delta-seconds que usa el backend). */
export function parseRetryAfter(header: string | null): number | null {
  if (!header) return null;
  const seconds = Number(header.trim());
  return Number.isFinite(seconds) && seconds >= 0 ? Math.ceil(seconds) : null;
}

function waitText(seconds: number): string {
  if (seconds < 60) return `${seconds} s`;
  const minutes = Math.ceil(seconds / 60);
  return `${minutes} min`;
}

/**
 * Mensaje en español para la UI. Los `title` del backend están en inglés y son genéricos a propósito
 * (no revelan si una cuenta existe), así que aquí se traduce por código de estado.
 */
export function describeApiError(error: unknown, overrides: Partial<Record<number, string>> = {}): string {
  if (error instanceof NetworkError) return 'No pudimos conectar con el servidor. Comprueba tu conexión e inténtalo de nuevo.';
  if (!(error instanceof ApiError)) return 'Ha ocurrido un error inesperado.';
  const override = overrides[error.status];
  if (override) return override;
  switch (error.status) {
    case 400:
      return 'Revisa los datos enviados.';
    case 401:
      return 'Tu sesión no es válida o caducó. Vuelve a iniciar sesión.';
    case 403:
      return 'No tienes permiso para realizar esta acción.';
    case 404:
      return 'El recurso no existe o no está disponible.';
    case 409:
      return 'La operación no es posible en el estado actual.';
    case 429:
      return error.retryAfterSeconds !== null
        ? `Demasiados intentos. Vuelve a intentarlo en ${waitText(error.retryAfterSeconds)}.`
        : 'Demasiados intentos. Espera unos minutos antes de volver a intentarlo.';
    default:
      return error.status >= 500 ? 'El servidor no pudo completar la operación. Inténtalo de nuevo más tarde.' : 'Ha ocurrido un error inesperado.';
  }
}
