import {
  Building2,
  ClipboardList,
  KeyRound,
  Layers,
  LayoutDashboard,
  MailPlus,
  ShieldCheck,
  Tablet,
  UserCircle,
  Users,
  type LucideIcon,
} from 'lucide-react';
import { ADMIN_PERMISSIONS, Permissions, type Permission } from '../../auth/permissions';

export interface NavItem {
  to: string;
  label: string;
  icon: LucideIcon;
  /** Se muestra si el usuario tiene alguno de estos permisos reales. Sin lista: cualquier usuario autenticado. */
  anyOf?: readonly Permission[];
  /** Pantalla con datos de demostración hasta que exista su API en el backend. */
  demo?: boolean;
}

export interface NavSection {
  title: string;
  items: NavItem[];
}

/**
 * Permiso provisional de las pantallas DEMO (clínicas, personal, catálogos): el backend aún no define permisos
 * ni endpoints para ellas, así que se limitan a quien administra cuentas.
 */
export const DEMO_ADMIN_PERMISSIONS: readonly Permission[] = [Permissions.Users.Manage];

/** Estructura del menú lateral del portal de administración; el Header la usa también para las migas de pan. */
export const NAV_SECTIONS: NavSection[] = [
  { title: 'Análisis', items: [{ to: '/admin/dashboard', label: 'Dashboard', icon: LayoutDashboard, anyOf: ADMIN_PERMISSIONS, demo: true }] },
  {
    title: 'Accesos',
    items: [
      { to: '/admin/usuarios', label: 'Usuarios', icon: Users, anyOf: [Permissions.Users.Manage] },
      { to: '/admin/roles', label: 'Roles y permisos', icon: KeyRound, anyOf: [Permissions.Users.Manage] },
      {
        to: '/admin/invitaciones',
        label: 'Invitaciones',
        icon: MailPlus,
        anyOf: [Permissions.Users.Manage, Permissions.Accounts.InvitePatient],
      },
    ],
  },
  {
    title: 'Operación',
    items: [
      { to: '/admin/clinicas', label: 'Clínicas', icon: Building2, anyOf: DEMO_ADMIN_PERMISSIONS, demo: true },
      { to: '/admin/personal', label: 'Personal', icon: ClipboardList, anyOf: DEMO_ADMIN_PERMISSIONS, demo: true },
      { to: '/admin/dispositivos', label: 'Dispositivos', icon: Tablet, anyOf: [Permissions.Devices.Manage] },
      { to: '/admin/catalogos', label: 'Catálogos', icon: Layers, anyOf: DEMO_ADMIN_PERMISSIONS, demo: true },
    ],
  },
  { title: 'Cumplimiento', items: [{ to: '/admin/auditoria', label: 'Auditoría', icon: ShieldCheck, anyOf: [Permissions.Audit.Read], demo: true }] },
  { title: 'Cuenta', items: [{ to: '/admin/perfil', label: 'Mi perfil', icon: UserCircle }] },
];

/** Primera pantalla del portal a la que el usuario tiene acceso. */
export function firstAdminRoute(canAny: (permissions: readonly string[]) => boolean): string {
  const item = NAV_SECTIONS.flatMap((s) => s.items).find((i) => i.anyOf && canAny(i.anyOf));
  return item?.to ?? '/admin/perfil';
}

export interface NavEntry {
  section: string;
  page: string;
  to: string;
  demo: boolean;
}

export function findNavEntry(pathname: string): NavEntry | null {
  for (const section of NAV_SECTIONS) {
    const item = section.items.find((i) => pathname === i.to || pathname.startsWith(`${i.to}/`));
    if (item) return { section: section.title, page: item.label, to: item.to, demo: item.demo ?? false };
  }
  return null;
}
