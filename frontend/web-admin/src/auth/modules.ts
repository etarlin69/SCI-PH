import { ADMIN_PERMISSIONS, HOSPITAL_PERMISSIONS, Permissions, type Permission } from './permissions';

/**
 * Módulos montados en esta SPA. Deciden a dónde ir tras el login y qué cambios de módulo ofrecer, sin depender de
 * la navegación de ningún módulo.
 * - `anyOf`: algún permiso que da acceso al módulo.
 * - `homeAnyOf`: permisos que hacen de este módulo el destino principal tras el login. Así un Receptionist (que
 *   tiene Accounts.InvitePatient, permiso de Admin) aterriza en Hospital, y un Administrator en Admin.
 */
export interface AppModule {
  key: string;
  label: string;
  home: string;
  anyOf: readonly Permission[];
  homeAnyOf: readonly Permission[];
}

export const APP_MODULES: AppModule[] = [
  {
    key: 'admin',
    label: 'Administración',
    home: '/admin',
    anyOf: ADMIN_PERMISSIONS,
    homeAnyOf: [Permissions.Users.Manage, Permissions.Devices.Manage, Permissions.Audit.Read],
  },
  { key: 'hospital', label: 'Hospital', home: '/hospital', anyOf: HOSPITAL_PERMISSIONS, homeAnyOf: HOSPITAL_PERMISSIONS },
];

/** Pantalla segura cuando la cuenta no tiene acceso a ningún módulo (p. ej. un Patient en la web). */
export const NO_MODULE_ROUTE = '/403';

const hasAny = (permissions: readonly string[], required: readonly string[]) => required.some((p) => permissions.includes(p));

export function resolveHomeRoute(permissions: readonly string[], modules: readonly AppModule[] = APP_MODULES): string {
  const match = modules.find((m) => hasAny(permissions, m.homeAnyOf)) ?? modules.find((m) => hasAny(permissions, m.anyOf));
  return match?.home ?? NO_MODULE_ROUTE;
}

/** Módulos a los que el usuario puede acceder (para ofrecer el cambio de módulo). */
export function accessibleModules(permissions: readonly string[], modules: readonly AppModule[] = APP_MODULES): AppModule[] {
  return modules.filter((m) => hasAny(permissions, m.anyOf));
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
