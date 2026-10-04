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
import type { ModuleKey } from '../../types';

export interface NavItem {
  to: string;
  label: string;
  icon: LucideIcon;
  module?: ModuleKey;
}

export interface NavSection {
  title: string;
  items: NavItem[];
}

/** Estructura del menú lateral; el Header la usa también para las migas de pan. */
export const NAV_SECTIONS: NavSection[] = [
  { title: 'Análisis', items: [{ to: '/admin/dashboard', label: 'Dashboard', icon: LayoutDashboard, module: 'dashboard' }] },
  {
    title: 'Accesos',
    items: [
      { to: '/admin/usuarios', label: 'Usuarios', icon: Users, module: 'users' },
      { to: '/admin/roles', label: 'Roles y permisos', icon: KeyRound, module: 'roles' },
      { to: '/admin/invitaciones', label: 'Invitaciones', icon: MailPlus, module: 'invitations' },
    ],
  },
  {
    title: 'Operación',
    items: [
      { to: '/admin/clinicas', label: 'Clínicas', icon: Building2, module: 'clinics' },
      { to: '/admin/personal', label: 'Personal', icon: ClipboardList, module: 'staff' },
      { to: '/admin/dispositivos', label: 'Dispositivos', icon: Tablet, module: 'devices' },
      { to: '/admin/catalogos', label: 'Catálogos', icon: Layers, module: 'catalogs' },
    ],
  },
  { title: 'Cumplimiento', items: [{ to: '/admin/auditoria', label: 'Auditoría', icon: ShieldCheck, module: 'audit' }] },
  { title: 'Cuenta', items: [{ to: '/admin/perfil', label: 'Mi perfil', icon: UserCircle }] },
];

export interface NavEntry {
  section: string;
  page: string;
  to: string;
}

export function findNavEntry(pathname: string): NavEntry | null {
  for (const section of NAV_SECTIONS) {
    const item = section.items.find((i) => pathname === i.to || pathname.startsWith(`${i.to}/`));
    if (item) return { section: section.title, page: item.label, to: item.to };
  }
  return null;
}
