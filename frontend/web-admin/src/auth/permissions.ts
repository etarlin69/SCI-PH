// Espejo de SCI-PH.Application/Common/Security/Permissions.cs y SecurityRoles.cs. Solo nombres: los permisos DEL
// USUARIO llegan siempre del backend (WebSession.user.permissions / GET /api/web/auth/me) y el backend vuelve a
// autorizar cada petición. Ocultar un botón o una ruta aquí es solo UX.

export const Permissions = {
  Users: { Manage: 'Users.Manage' },
  Accounts: { InvitePatient: 'Accounts.InvitePatient' },
  Devices: { Manage: 'Devices.Manage' },
  Audit: { Read: 'Audit.Read' },
  Patients: { Read: 'Patients.Read', Create: 'Patients.Create', Update: 'Patients.Update', Identify: 'Patients.Identify' },
  Encounters: { Read: 'Encounters.Read', Create: 'Encounters.Create', Update: 'Encounters.Update', Close: 'Encounters.Close' },
  Triage: { Write: 'Triage.Write' },
  VitalSigns: { Write: 'VitalSigns.Write' },
  ClinicalNotes: { Write: 'ClinicalNotes.Write' },
  Diagnoses: { Write: 'Diagnoses.Write' },
  Procedures: { Write: 'Procedures.Write' },
  Results: { Write: 'Results.Write' },
  Medications: { Read: 'Medications.Read' },
  Prescriptions: { Write: 'Prescriptions.Write' },
  MedicationAdministrations: { Write: 'MedicationAdministrations.Write' },
  Transfers: { Read: 'Transfers.Read', Manage: 'Transfers.Manage' },
  Documents: { Read: 'Documents.Read', Upload: 'Documents.Upload' },
} as const;

type ValuesOf<T> = T[keyof T];
export type Permission = ValuesOf<{ [K in keyof typeof Permissions]: ValuesOf<(typeof Permissions)[K]> }>;

/** Permisos de las pantallas del portal de administración. */
export const ADMIN_PERMISSIONS: readonly Permission[] = [
  Permissions.Users.Manage,
  Permissions.Accounts.InvitePatient,
  Permissions.Devices.Manage,
  Permissions.Audit.Read,
];

/**
 * Permisos que dan acceso al módulo hospitalario (cualquiera de ellos). Cada pantalla exige además el suyo
 * (ver HOSPITAL_SCREEN_PERMISSIONS en components/common/navigation.ts).
 */
export const HOSPITAL_PERMISSIONS: readonly Permission[] = [
  Permissions.Patients.Read,
  Permissions.Encounters.Read,
  Permissions.Encounters.Create,
  Permissions.Transfers.Read,
];

export const SecurityRoles = ['Administrator', 'Doctor', 'Nurse', 'Paramedic', 'Receptionist', 'Patient'] as const;
export type SecurityRole = (typeof SecurityRoles)[number];

/** SecurityRoles.InvitableStaff: Administrator nunca se concede por invitación. */
export const INVITABLE_STAFF_ROLES: readonly SecurityRole[] = ['Doctor', 'Nurse', 'Paramedic', 'Receptionist'];

/** Etiquetas en español. Las claves son los valores reales del backend. */
export const ROLE_LABELS: Record<SecurityRole, string> = {
  Administrator: 'Administrador',
  Doctor: 'Médico',
  Nurse: 'Enfermería',
  Paramedic: 'Paramédico',
  Receptionist: 'Recepción',
  Patient: 'Paciente',
};

export const isSecurityRole = (value: string): value is SecurityRole => (SecurityRoles as readonly string[]).includes(value);

/** Etiqueta de un rol recibido del backend; si es desconocido se muestra tal cual. */
export const roleLabel = (role: string): string => (isSecurityRole(role) ? ROLE_LABELS[role] : role);
