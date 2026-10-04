export type ServiceErrorCode =
  | 'NETWORK'
  | 'SERVER'
  | 'NOT_FOUND'
  | 'FORBIDDEN'
  | 'CONFLICT'
  | 'VALIDATION'
  | 'RATE_LIMITED'
  | 'IN_USE'
  | 'INVALID_CREDENTIALS'
  | 'ACCOUNT_LOCKED'
  | 'INVALID_CODE'
  | 'TOKEN_EXPIRED'
  | 'PASSWORD_REUSED';

/** Error normalizado de la capa de servicios. */
export class ServiceError extends Error {
  readonly code: ServiceErrorCode;
  readonly details: Record<string, unknown>;

  constructor(code: ServiceErrorCode, message: string, details: Record<string, unknown> = {}) {
    super(message);
    this.name = 'ServiceError';
    this.code = code;
    this.details = details;
  }
}

export function isServiceError(e: unknown, code?: ServiceErrorCode): e is ServiceError {
  return e instanceof ServiceError && (code === undefined || e.code === code);
}

/** Mensaje legible para mostrar en banners y toasts. */
export function errorMessage(e: unknown): string {
  if (e instanceof ServiceError) return e.message;
  if (e instanceof Error) return e.message;
  return 'Ha ocurrido un error inesperado.';
}
