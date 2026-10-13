import {
  Ambulance,
  Building2,
  ClipboardList,
  ClipboardPlus,
  KeyRound,
  Layers,
  LayoutDashboard,
  MailPlus,
  ShieldCheck,
  Siren,
  Stethoscope,
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
 * Permiso provisional de las pantallas DEMO que quedan (catálogos): el backend aún no define permisos
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
      { to: '/admin/clinicas', label: 'Clínicas', icon: Building2, anyOf: [Permissions.Clinics.Manage] },
      { to: '/admin/personal', label: 'Personal', icon: ClipboardList, anyOf: [Permissions.ClinicalStaff.Manage] },
      { to: '/admin/dispositivos', label: 'Dispositivos', icon: Tablet, anyOf: [Permissions.Devices.Manage] },
      { to: '/admin/catalogos', label: 'Catálogos', icon: Layers, anyOf: DEMO_ADMIN_PERMISSIONS, demo: true },
    ],
  },
  { title: 'Cumplimiento', items: [{ to: '/admin/auditoria', label: 'Auditoría', icon: ShieldCheck, anyOf: [Permissions.Audit.Read], demo: true }] },
  { title: 'Cuenta', items: [{ to: '/admin/perfil', label: 'Mi perfil', icon: UserCircle }] },
];

/**
 * Permisos reales de cada pantalla hospitalaria (cualquiera de la lista). Las acciones de escritura dentro de cada
 * pantalla comprueban además su permiso concreto (Triage.Write, Prescriptions.Write, Encounters.Close…).
 */
export const HOSPITAL_SCREEN_PERMISSIONS = {
  reception: [Permissions.Encounters.Create, Permissions.Patients.Read],
  patients: [Permissions.Patients.Read],
  encounters: [Permissions.Encounters.Read],
  triage: [Permissions.Triage.Write],
  medication: [Permissions.Medications.Read, Permissions.Prescriptions.Write, Permissions.MedicationAdministrations.Write],
  transfers: [Permissions.Transfers.Read],
} satisfies Record<string, readonly Permission[]>;

/** Menú del módulo hospitalario (Rol 4). */
export const HOSPITAL_NAV_SECTIONS: NavSection[] = [
  {
    title: 'Hospital',
    items: [
      { to: '/hospital/recepcion', label: 'Recepción', icon: ClipboardPlus, anyOf: HOSPITAL_SCREEN_PERMISSIONS.reception },
      { to: '/hospital/pacientes', label: 'Pacientes', icon: Users, anyOf: HOSPITAL_SCREEN_PERMISSIONS.patients },
      { to: '/hospital/atenciones', label: 'Atenciones', icon: Stethoscope, anyOf: HOSPITAL_SCREEN_PERMISSIONS.encounters },
      { to: '/hospital/triage', label: 'Triage', icon: Siren, anyOf: HOSPITAL_SCREEN_PERMISSIONS.triage },
      { to: '/hospital/transferencias', label: 'Transferencias', icon: Ambulance, anyOf: HOSPITAL_SCREEN_PERMISSIONS.transfers },
    ],
  },
  { title: 'Cuenta', items: [{ to: '/hospital/perfil', label: 'Mi perfil', icon: UserCircle }] },
];

export type NavModuleKey = 'admin' | 'hospital';

export interface ModuleNav {
  key: NavModuleKey;
  label: string;
  subtitle: string;
  home: string;
  sections: NavSection[];
}

export const MODULE_NAV: Record<NavModuleKey, ModuleNav> = {
  admin: { key: 'admin', label: 'Administración', subtitle: 'Administración y Analítica', home: '/admin/dashboard', sections: NAV_SECTIONS },
  hospital: { key: 'hospital', label: 'Hospital', subtitle: 'Atención hospitalaria', home: '/hospital', sections: HOSPITAL_NAV_SECTIONS },
};

export function moduleForPath(pathname: string): NavModuleKey {
  return pathname === '/hospital' || pathname.startsWith('/hospital/') ? 'hospital' : 'admin';
}

/** Primera pantalla del portal a la que el usuario tiene acceso. */
export function firstAdminRoute(canAny: (permissions: readonly string[]) => boolean): string {
  const item = NAV_SECTIONS.flatMap((s) => s.items).find((i) => i.anyOf && canAny(i.anyOf));
  return item?.to ?? '/admin/perfil';
}

/** Primera pantalla hospitalaria permitida (`/hospital` → recepción si se puede). */
export function firstHospitalRoute(canAny: (permissions: readonly string[]) => boolean): string {
  const item = HOSPITAL_NAV_SECTIONS.flatMap((s) => s.items).find((i) => i.anyOf && canAny(i.anyOf));
  return item?.to ?? '/hospital/perfil';
}

export interface NavEntry {
  section: string;
  page: string;
  to: string;
  demo: boolean;
}

// Vistas de una atención: /hospital/atenciones/:id[/triage|/medicacion].
const ENCOUNTER_VIEW = /^\/hospital\/atenciones\/[^/]+(?:\/(triage|medicacion))?\/?$/;
const ENCOUNTER_VIEW_LABELS: Record<string, string> = { '': 'Expediente', triage: 'Triage', medicacion: 'Medicación y egreso' };

export function findNavEntry(pathname: string): NavEntry | null {
  const encounterView = ENCOUNTER_VIEW.exec(pathname);
  if (encounterView) {
    return { section: 'Atenciones', page: ENCOUNTER_VIEW_LABELS[encounterView[1] ?? ''], to: pathname, demo: false };
  }
  for (const section of MODULE_NAV[moduleForPath(pathname)].sections) {
    const item = section.items.find((i) => pathname === i.to || pathname.startsWith(`${i.to}/`));
    if (item) return { section: section.title, page: item.label, to: item.to, demo: item.demo ?? false };
  }
  return null;
}
