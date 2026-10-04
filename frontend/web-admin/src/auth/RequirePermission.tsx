import type { ReactNode } from 'react';
import { Outlet } from 'react-router-dom';
import { ForbiddenPage } from '../features/auth/pages/ForbiddenPage';
import type { ModuleKey, PermissionAction } from '../types';
import { useAuth } from './AuthContext';

const MODULE_SLUG: Record<ModuleKey, string> = {
  auth: 'autenticacion',
  dashboard: 'dashboard',
  users: 'usuarios',
  roles: 'roles',
  invitations: 'invitaciones',
  devices: 'dispositivos',
  clinics: 'clinicas',
  staff: 'personal',
  catalogs: 'catalogos',
  audit: 'auditoria',
};
const ACTION_LABEL: Record<PermissionAction, string> = { read: 'leer', create: 'crear', update: 'actualizar', delete: 'eliminar' };

export interface RequirePermissionProps {
  module: ModuleKey;
  action?: PermissionAction;
  children?: ReactNode;
}

/** Muestra la pantalla 403 (manteniendo la URL) si el rol no tiene el permiso. */
export function RequirePermission({ module, action = 'read', children }: RequirePermissionProps) {
  const { can } = useAuth();
  if (!can(module, action)) return <ForbiddenPage missingPermission={`${MODULE_SLUG[module]}:${ACTION_LABEL[action]}`} />;
  return children ? <>{children}</> : <Outlet />;
}
