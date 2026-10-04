import type { InvitationStatus } from '../../../types';
import { Badge, INVITATION_STATUS_TONE } from '../../../components/shared';

export interface InvitationStatusBadgeProps {
  estado: InvitationStatus;
}

export function InvitationStatusBadge({ estado }: InvitationStatusBadgeProps) {
  return <Badge tone={INVITATION_STATUS_TONE[estado]}>{estado}</Badge>;
}
