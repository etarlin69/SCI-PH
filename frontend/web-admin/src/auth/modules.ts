import { ADMIN_PERMISSIONS, type Permission } from './permissions';

/**
 * Módulos montados en esta SPA y el permiso que da acceso a cada uno. Decide a dónde ir tras el login sin
 * depender de la navegación de ningún módulo. El módulo hospitalario (Rol 4) se registra aquí al integrarse,
 * p. ej. `{ key: 'hospital', home: '/hospital', anyOf: [Permissions.Encounters.Read, …] }`.
 */
export interface AppModule {
  key: string;
  home: string;
  anyOf: readonly Permission[];
}

export const APP_MODULES: AppModule[] = [{ key: 'admin', home: '/admin', anyOf: ADMIN_PERMISSIONS }];

/** Pantalla segura cuando la cuenta no tiene acceso a ningún módulo (p. ej. un Patient en la web). */
export const NO_MODULE_ROUTE = '/403';

export function resolveHomeRoute(permissions: readonly string[], modules: readonly AppModule[] = APP_MODULES): string {
  const match = modules.find((m) => m.anyOf.some((p) => permissions.includes(p)));
  return match?.home ?? NO_MODULE_ROUTE;
}

/** Solo rutas internas relativas: evita open redirects con `//evil.example` o `https://…`. */
export function isSafeInternalPath(path: unknown): path is string {
  return typeof path === 'string' && path.startsWith('/') && !path.startsWith('//') && !path.startsWith('/\\');
}

const PUBLIC_PATHS = ['/login', '/mfa', '/forgot-password', '/reset-password', '/activate-account', '/confirm-email'];

/** Destino post-login: la ruta pedida si es interna y no es una pantalla pública; si no, el módulo permitido. */
export function postLoginRoute(permissions: readonly string[], from?: unknown): string {
  if (isSafeInternalPath(from) && from !== '/' && !PUBLIC_PATHS.some((p) => from === p || from.startsWith(`${p}?`))) return from;
  return resolveHomeRoute(permissions);
}
