import type { ModuleDefinition, ModuleKey, PermissionAction, PermissionMatrix, Role } from '../../types';
import { daysAgo } from './time';

export const PERMISSION_ACTIONS: { key: PermissionAction; label: string }[] = [
  { key: 'read', label: 'Leer' },
  { key: 'create', label: 'Crear' },
  { key: 'update', label: 'Actualizar' },
  { key: 'delete', label: 'Eliminar' },
];

export const MODULES: ModuleDefinition[] = [
  { key: 'auth', label: 'Autenticación', description: 'Sesiones activas, MFA y bloqueos de cuenta', notApplicable: [] },
  { key: 'dashboard', label: 'Dashboard', description: 'Indicadores y gráficos operativos', notApplicable: ['create', 'delete'] },
  { key: 'users', label: 'Usuarios', description: 'Cuentas, roles asignados y contraseñas temporales', sensitive: true, notApplicable: [] },
  { key: 'roles', label: 'Roles', description: 'Roles personalizados y esta matriz', sensitive: true, notApplicable: [] },
  { key: 'invitations', label: 'Invitaciones', description: 'Invitar pacientes y personal', notApplicable: [] },
  { key: 'devices', label: 'Dispositivos', description: 'Tablets y equipos de ambulancia', notApplicable: [] },
  { key: 'clinics', label: 'Clínicas', description: 'Centros, servicios y capacidad', notApplicable: [] },
  { key: 'staff', label: 'Personal', description: 'Perfil laboral vinculado a cada usuario', notApplicable: [] },
  { key: 'catalogs', label: 'Catálogos', description: 'Tablas maestras: CIE-10, medicamentos…', notApplicable: [] },
  { key: 'audit', label: 'Auditoría', description: 'Registro inalterable de eventos', notApplicable: ['create', 'update', 'delete'] },
];

export const MODULE_KEYS = MODULES.map((m) => m.key);

export const APPLICABLE_PERMISSIONS = MODULES.reduce((n, m) => n + 4 - m.notApplicable.length, 0);

const ALL: PermissionAction[] = ['read', 'create', 'update', 'delete'];

function matrix(partial: Partial<Record<ModuleKey, PermissionAction[]>>): PermissionMatrix {
  return Object.fromEntries(MODULE_KEYS.map((k) => [k, partial[k] ?? []])) as PermissionMatrix;
}

const FULL = matrix(
  Object.fromEntries(MODULES.map((m) => [m.key, ALL.filter((a) => !m.notApplicable.includes(a))])) as Record<ModuleKey, PermissionAction[]>,
);

export const ROLES_MOCK: Role[] = [
  {
    id: 'ROL-ADM',
    nombre: 'Administrador',
    descripcion: 'Acceso total al portal: usuarios, roles, catálogos y consulta de auditoría. MFA obligatorio.',
    tipo: 'Sistema',
    usuarios: 2,
    permisos: FULL,
    actualizadoEl: daysAgo(120),
  },
  {
    id: 'ROL-MED',
    nombre: 'Médico',
    descripcion: 'Consulta su sesión, el dashboard, clínicas, personal y catálogos.',
    tipo: 'Sistema',
    usuarios: 6,
    permisos: matrix({ auth: ['read'], dashboard: ['read'], clinics: ['read'], staff: ['read'], catalogs: ['read'] }),
    actualizadoEl: daysAgo(120),
  },
  {
    id: 'ROL-ENF',
    nombre: 'Enfermero',
    descripcion: 'Consulta su sesión, el dashboard, clínicas y catálogos.',
    tipo: 'Sistema',
    usuarios: 3,
    permisos: matrix({ auth: ['read'], dashboard: ['read'], clinics: ['read'], catalogs: ['read'] }),
    actualizadoEl: daysAgo(120),
  },
  {
    id: 'ROL-PAR',
    nombre: 'Paramédico',
    descripcion: 'Consulta su sesión, dispositivos de su unidad, clínicas y catálogos.',
    tipo: 'Sistema',
    usuarios: 5,
    permisos: matrix({ auth: ['read'], devices: ['read'], clinics: ['read'], catalogs: ['read'] }),
    actualizadoEl: daysAgo(120),
  },
  {
    id: 'ROL-COO',
    nombre: 'Coordinador',
    descripcion: 'Dashboard operativo, disponibilidad de clínicas e invitaciones de personal.',
    tipo: 'Sistema',
    usuarios: 2,
    permisos: matrix({
      auth: ['read'],
      dashboard: ['read', 'update'],
      clinics: ['read', 'update'],
      staff: ['read'],
      devices: ['read'],
      catalogs: ['read'],
      invitations: ['read', 'create'],
    }),
    actualizadoEl: daysAgo(120),
  },
  {
    id: 'ROL-OPE',
    nombre: 'Operador',
    descripcion: 'Despacho: dashboard y estado de los dispositivos de ambulancia.',
    tipo: 'Sistema',
    usuarios: 2,
    permisos: matrix({ auth: ['read'], dashboard: ['read'], devices: ['read', 'update'], clinics: ['read'], catalogs: ['read'] }),
    actualizadoEl: daysAgo(120),
  },
  {
    id: 'ROL-AUD',
    nombre: 'Auditor externo',
    descripcion: 'Solo lectura de dashboard, roles y auditoría para revisiones de cumplimiento.',
    tipo: 'Personalizado',
    usuarios: 1,
    permisos: matrix({ dashboard: ['read'], roles: ['read'], audit: ['read'] }),
    actualizadoEl: daysAgo(18),
  },
];

export const SYSTEM_ROLE_NAMES = ROLES_MOCK.filter((r) => r.tipo === 'Sistema').map((r) => r.nombre);
