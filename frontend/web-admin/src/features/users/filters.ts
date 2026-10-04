import type { User, UserFilters, UserStatus } from '../../types';

export const EMPTY_USER_FILTERS: UserFilters = { q: '', rol: '', estado: '' };

export const USER_STATUSES: UserStatus[] = ['Activo', 'Pendiente', 'Suspendido', 'Inactivo'];

/** Búsqueda por nombre/email + rol + estado. */
export function filterUsers(users: User[], f: UserFilters, ignoreStatus = false): User[] {
  const q = f.q.trim().toLowerCase();
  return users.filter(
    (u) =>
      (!q || `${u.nombre} ${u.email}`.toLowerCase().includes(q)) &&
      (!f.rol || u.rol === f.rol) &&
      (ignoreStatus || !f.estado || u.estado === f.estado),
  );
}

export const activeUserFilterCount = (f: UserFilters) => [f.q, f.rol, f.estado].filter(Boolean).length;
