import type { DeviceStatus } from '../../../types';
import { Badge, DEVICE_STATUS_TONE } from '../../../components/shared';

export interface DeviceStatusBadgeProps {
  estado: DeviceStatus;
  className?: string;
}

/** Etiqueta de estado de un dispositivo. */
export function DeviceStatusBadge({ estado, className }: DeviceStatusBadgeProps) {
  return (
    <Badge tone={DEVICE_STATUS_TONE[estado]} className={className}>
      {estado}
    </Badge>
  );
}
