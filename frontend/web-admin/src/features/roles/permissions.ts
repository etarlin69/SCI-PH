import { PERMISSION_ACTIONS } from '../../data/mock';
import type { ModuleDefinition, ModuleKey, PermissionAction, PermissionChange, PermissionMatrix } from '../../types';

export const ACTION_LABEL: Record<PermissionAction, string> = Object.fromEntries(PERMISSION_ACTIONS.map((a) => [a.key, a.label])) as Record<
  PermissionAction,
  string
>;

export const countPermissions = (m: PermissionMatrix) => Object.values(m).reduce((n, actions) => n + actions.length, 0);

export const has = (m: PermissionMatrix, module: ModuleKey, action: PermissionAction) => m[module]?.includes(action) ?? false;

/** Activa/desactiva un permiso respetando la dependencia "Crear, Actualizar y Eliminar requieren Leer": conceder C/U/D concede Leer; retirar Leer retira todo el módulo. */
export function togglePermission(m: PermissionMatrix, module: ModuleDefinition, action: PermissionAction): PermissionMatrix {
  if (module.notApplicable.includes(action)) return m;
  const current = new Set(m[module.key] ?? []);
  if (current.has(action)) {
    if (action === 'read') current.clear();
    else current.delete(action);
  } else {
    current.add(action);
    current.add('read');
  }
  return { ...m, [module.key]: PERMISSION_ACTIONS.map((a) => a.key).filter((a) => current.has(a)) };
}

/** Concede o retira todas las acciones aplicables de un módulo. */
export function setModuleAll(m: PermissionMatrix, module: ModuleDefinition, granted: boolean): PermissionMatrix {
  const all = PERMISSION_ACTIONS.map((a) => a.key).filter((a) => !module.notApplicable.includes(a));
  return { ...m, [module.key]: granted ? all : [] };
}

export function diffPermissions(before: PermissionMatrix, after: PermissionMatrix, modules: ModuleDefinition[]): PermissionChange[] {
  const changes: PermissionChange[] = [];
  modules.forEach((mod) =>
    PERMISSION_ACTIONS.forEach(({ key }) => {
      const a = has(before, mod.key, key);
      const b = has(after, mod.key, key);
      if (a !== b) changes.push({ module: mod.key, action: key, granted: b });
    }),
  );
  return changes;
}
