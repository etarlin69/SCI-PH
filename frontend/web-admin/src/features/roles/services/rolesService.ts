import { apiRequest } from '../../../api';
import type { RoleInfo } from '../types/roles.types';

/**
 * Roles y permisos (Users.Manage). Solo lectura: la matriz rol → permisos es código del backend
 * (RolePermissions); no existen endpoints para crear, renombrar, borrar roles ni editar permisos.
 */
export const rolesService = {
  list: (signal?: AbortSignal) => apiRequest<RoleInfo[]>('/api/web/admin/roles', { signal }),
};
