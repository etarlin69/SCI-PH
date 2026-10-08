import { apiRequest } from '../../../api';
import type { UserListQuery, UserPage, UserSummary } from '../types/users.types';

const BASE = '/api/web/admin/users';

/**
 * Administración de cuentas (permiso Users.Manage). Cada cambio termina las sesiones de la cuenta afectada.
 * El backend rechaza con 409 dejar el sistema sin administradores o quitarse el propio acceso de administrador.
 */
export const usersService = {
  list: ({ search, page = 1, pageSize = 20 }: UserListQuery = {}, signal?: AbortSignal) =>
    apiRequest<UserPage>(BASE, { query: { search: search?.trim() || undefined, page, pageSize }, signal }),

  get: (id: string, signal?: AbortSignal) => apiRequest<UserSummary>(`${BASE}/${encodeURIComponent(id)}`, { signal }),

  activate: (id: string) => apiRequest<void>(`${BASE}/${encodeURIComponent(id)}/activate`, { method: 'POST' }),

  deactivate: (id: string) => apiRequest<void>(`${BASE}/${encodeURIComponent(id)}/deactivate`, { method: 'POST' }),

  /** Reemplaza los roles. Solo roles predefinidos; Patient no se combina con otros. */
  setRoles: (id: string, roles: string[]) => apiRequest<void>(`${BASE}/${encodeURIComponent(id)}/roles`, { method: 'PUT', body: { roles } }),

  revokeSessions: (id: string) => apiRequest<void>(`${BASE}/${encodeURIComponent(id)}/revoke-sessions`, { method: 'POST' }),
};
