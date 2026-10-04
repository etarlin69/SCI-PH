import { MODULES, ROLES_MOCK, USERS_MOCK } from '../../../data/mock';
import type { ModuleDefinition, PermissionMatrix, Role, RoleInput } from '../../../types';
import { ServiceError } from '../../../services/core/errors';
import { mockList, mockRead, mockWrite, notFound } from '../../../services/core/mock';
import { record } from '../../audit/services/auditService';

const roles: Role[] = ROLES_MOCK.map((r) => ({ ...r, usuarios: USERS_MOCK.filter((u) => u.rol === r.nombre).length }));

const today = () => new Date().toISOString().slice(0, 10);

function find(id: string): Role {
  return roles.find((r) => r.id === id) ?? notFound('El rol');
}

export const rolesService = {
  list(): Promise<Role[]> {
    return mockList(() => roles);
  },

  get(id: string): Promise<Role> {
    return mockRead(() => find(id));
  },

  modules(): Promise<ModuleDefinition[]> {
    return mockRead(() => MODULES);
  },

  create(input: RoleInput): Promise<Role> {
    return mockWrite('roles.create', () => {
      if (roles.some((r) => r.nombre.toLowerCase() === input.nombre.trim().toLowerCase())) {
        throw new ServiceError('CONFLICT', 'Ya existe un rol con ese nombre.', { field: 'nombre' });
      }
      const base = input.copiarDe ? roles.find((r) => r.id === input.copiarDe) : undefined;
      const empty = Object.fromEntries(MODULES.map((m) => [m.key, []])) as unknown as PermissionMatrix;
      const role: Role = {
        id: `ROL-${Date.now().toString(36).toUpperCase()}`,
        nombre: input.nombre.trim(),
        descripcion: input.descripcion.trim() || 'Rol personalizado.',
        tipo: 'Personalizado',
        usuarios: 0,
        permisos: base ? structuredClone(base.permisos) : empty,
        actualizadoEl: today(),
      };
      roles.push(role);
      record({
        accion: 'Crear',
        recurso: `Rol ${role.nombre}`,
        descripcion: base ? `Rol creado copiando los permisos de ${base.nombre}.` : 'Rol creado sin permisos.',
      });
      return role;
    });
  },

  update(id: string, input: Pick<RoleInput, 'nombre' | 'descripcion'>): Promise<Role> {
    return mockWrite('roles.update', () => {
      const role = find(id);
      if (role.tipo === 'Sistema') throw new ServiceError('FORBIDDEN', 'Los roles del sistema son de solo lectura.');
      if (roles.some((r) => r.id !== id && r.nombre.toLowerCase() === input.nombre.trim().toLowerCase())) {
        throw new ServiceError('CONFLICT', 'Ya existe un rol con ese nombre.', { field: 'nombre' });
      }
      Object.assign(role, { nombre: input.nombre.trim(), descripcion: input.descripcion.trim(), actualizadoEl: today() });
      record({ accion: 'Actualizar', recurso: `Rol ${role.nombre}`, descripcion: 'Nombre y descripción del rol actualizados.' });
      return role;
    });
  },

  /** Guarda la matriz completa de un rol personalizado. */
  updatePermissions(id: string, permisos: PermissionMatrix): Promise<Role> {
    return mockWrite('roles.permissions', () => {
      const role = find(id);
      if (role.tipo === 'Sistema') throw new ServiceError('FORBIDDEN', 'Los roles del sistema son de solo lectura.');
      role.permisos = permisos;
      role.actualizadoEl = today();
      record({ accion: 'Actualizar', recurso: `Rol ${role.nombre}`, descripcion: 'Cambio en la matriz de permisos.' });
      return role;
    });
  },

  remove(id: string): Promise<void> {
    return mockWrite('roles.remove', () => {
      const role = find(id);
      if (role.tipo === 'Sistema') throw new ServiceError('FORBIDDEN', 'Los roles del sistema no se pueden eliminar.');
      if (role.usuarios > 0) throw new ServiceError('IN_USE', `Reasigna sus ${role.usuarios} usuario(s) antes de eliminarlo.`);
      roles.splice(roles.indexOf(role), 1);
      record({ accion: 'Eliminar', recurso: `Rol ${role.nombre}`, descripcion: 'Rol personalizado eliminado.' });
    });
  },

  /** Ajusta el contador cuando cambia el rol de un usuario (solo mock). */
  _syncCounts(): void {
    roles.forEach((r) => (r.usuarios = USERS_MOCK.filter((u) => u.rol === r.nombre).length));
  },
};
