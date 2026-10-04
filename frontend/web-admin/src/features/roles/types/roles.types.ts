import type { ISODate } from '../../../types/common';

export type PermissionAction = 'read' | 'create' | 'update' | 'delete';

export type ModuleKey = 'auth' | 'dashboard' | 'users' | 'roles' | 'invitations' | 'devices' | 'clinics' | 'staff' | 'catalogs' | 'audit';

export type PermissionMatrix = Record<ModuleKey, PermissionAction[]>;

export type RoleType = 'Sistema' | 'Personalizado';

export interface Role {
  id: string;
  nombre: string;
  descripcion: string;
  tipo: RoleType;
  usuarios: number;
  permisos: PermissionMatrix;
  actualizadoEl: ISODate;
}

export interface ModuleDefinition {
  key: ModuleKey;
  label: string;
  description: string;
  sensitive?: boolean;
  notApplicable: PermissionAction[];
}

export interface RoleInput {
  nombre: string;
  descripcion: string;
  copiarDe?: string;
}

export interface PermissionChange {
  module: ModuleKey;
  action: PermissionAction;
  granted: boolean;
}
