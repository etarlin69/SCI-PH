/** RFC 7807 tal como lo emite ASP.NET Core (`Problem()` / `ValidationProblem()`), con extensiones como `traceId`. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  /** Solo en ValidationProblemDetails: campo → mensajes. */
  errors?: Record<string, string[]>;
  [extension: string]: unknown;
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
  const v = value as Record<string, unknown>;
  return typeof v.title === 'string' || typeof v.status === 'number' || typeof v.type === 'string';
}

/** Normaliza `errors` a un mapa con claves en camelCase (el backend usa los nombres de propiedad C#). */
export function validationErrors(problem: ProblemDetails | null): Record<string, string[]> {
  if (!problem?.errors || typeof problem.errors !== 'object') return {};
  const result: Record<string, string[]> = {};
  for (const [key, messages] of Object.entries(problem.errors)) {
    if (!Array.isArray(messages)) continue;
    const field = key ? key.charAt(0).toLowerCase() + key.slice(1) : '';
    result[field] = messages.filter((m): m is string => typeof m === 'string');
  }
  return result;
}
