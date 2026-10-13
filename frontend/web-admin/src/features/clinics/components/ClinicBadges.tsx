import { Badge, type BadgeTone } from '../../../components/shared';
import type { ClinicStatus, ClinicType } from '../types/clinics.types';

export const CLINIC_TYPE_LABELS: Record<ClinicType, string> = {
  Hospital: 'Hospital',
  Clinic: 'Clínica',
  PrimaryCareCenter: 'Centro de atención primaria',
  EmergencyCenter: 'Centro de emergencias',
  Other: 'Otro',
};

const STATUS: Record<ClinicStatus, { label: string; tone: BadgeTone }> = {
  Active: { label: 'Activa', tone: 'success' },
  Inactive: { label: 'Inactiva', tone: 'neutral' },
};

export function ClinicStatusBadge({ status }: { status: ClinicStatus }) {
  return (
    <Badge tone={STATUS[status].tone} dot>
      {STATUS[status].label}
    </Badge>
  );
}
