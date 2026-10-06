/** RoleResponse de GET /api/web/admin/roles: rol predefinido y sus permisos (RolePermissions del backend). */
export interface RoleInfo {
  name: string;
  permissions: string[];
}
