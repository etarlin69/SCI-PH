import type { StaffStatus } from '../../../types';
import { Badge, STAFF_STATUS_TONE } from '../../../components/shared';

export interface StaffStatusBadgeProps {
  estado: StaffStatus;
}

export function StaffStatusBadge({ estado }: StaffStatusBadgeProps) {
  return <Badge tone={STAFF_STATUS_TONE[estado]}>{estado}</Badge>;
}
