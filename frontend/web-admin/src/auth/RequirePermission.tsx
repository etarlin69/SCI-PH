import type { ReactNode } from 'react';
import { Outlet } from 'react-router-dom';
import { ForbiddenPage } from '../features/auth/pages/ForbiddenPage';
import { useAuth } from './AuthContext';
import type { Permission } from './permissions';

export type RequirePermissionProps = {
  children?: ReactNode;
} & ({ permission: Permission; anyOf?: never } | { anyOf: readonly Permission[]; permission?: never });

/**
 * Muestra el 403 (sin cambiar la URL ni cerrar la sesión) si el usuario no tiene el permiso. Solo es UX:
 * el backend vuelve a autorizar cada petición.
 */
export function RequirePermission({ permission, anyOf, children }: RequirePermissionProps) {
  const { can, canAny } = useAuth();
  const required = permission ? [permission] : (anyOf ?? []);
  const allowed = permission ? can(permission) : canAny(required);
  if (!allowed) return <ForbiddenPage missingPermissions={required} />;
  return children ? <>{children}</> : <Outlet />;
}
