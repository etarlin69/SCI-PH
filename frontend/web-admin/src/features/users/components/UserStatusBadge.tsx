import type { UserStatus } from '../../../types';
import { Badge, USER_STATUS_TONE } from '../../../components/shared';

export interface UserStatusBadgeProps {
  estado: UserStatus;
}

export function UserStatusBadge({ estado }: UserStatusBadgeProps) {
  return <Badge tone={USER_STATUS_TONE[estado]}>{estado}</Badge>;
}
