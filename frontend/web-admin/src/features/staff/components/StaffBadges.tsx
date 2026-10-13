import { Badge } from '../../../components/shared';
import type { SecurityRole } from '../../../auth';
import type { ClinicalStaffType, StaffStatus } from '../types/staff.types';

export const STAFF_TYPE_LABELS: Record<ClinicalStaffType, string> = {
  Physician: 'Médico',
  Nurse: 'Enfermería',
  Paramedic: 'Paramédico',
  MedicalAssistant: 'Asistente médico',
  OtherClinical: 'Otro personal clínico',
};

/**
 * Sugerencia de rol de la cuenta según el tipo de personal (solo UX: el backend no la valida y el administrador
 * elige siempre entre los roles invitables). Sin sugerencia para asistentes y otro personal.
 */
export const SUGGESTED_ROLE: Partial<Record<ClinicalStaffType, SecurityRole>> = {
  Physician: 'Doctor',
  Nurse: 'Nurse',
  Paramedic: 'Paramedic',
};

export function StaffStatusBadge({ status }: { status: StaffStatus }) {
  return (
    <Badge tone={status === 'Active' ? 'success' : 'neutral'} dot>
      {status === 'Active' ? 'Activo' : 'Inactivo'}
    </Badge>
  );
}

export function AccountBadge({ linked }: { linked: boolean }) {
  return <Badge tone={linked ? 'info' : 'warning'}>{linked ? 'Cuenta vinculada' : 'Sin cuenta'}</Badge>;
}

export const staffFullName = (s: { givenNames: string; familyNames: string }) => `${s.givenNames} ${s.familyNames}`;
