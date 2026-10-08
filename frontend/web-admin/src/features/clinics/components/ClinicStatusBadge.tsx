import type { ClinicStatus } from '../../../types';
import { Badge, CLINIC_STATUS_TONE } from '../../../components/shared';

export interface ClinicStatusBadgeProps {
  estado: ClinicStatus;
  className?: string;
}

/** Badge de estado de una clínica con el tono estándar del sistema. */
export function ClinicStatusBadge({ estado, className }: ClinicStatusBadgeProps) {
  return (
    <Badge tone={CLINIC_STATUS_TONE[estado]} className={className}>
      {estado}
    </Badge>
  );
}
