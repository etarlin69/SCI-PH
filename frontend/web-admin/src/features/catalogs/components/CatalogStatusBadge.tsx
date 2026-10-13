import { Badge, CATALOG_STATUS_TONE } from '../../../components/shared';
import type { AmbulanceStatus, CatalogStatus } from '../../../types';

export interface CatalogStatusBadgeProps {
  estado: CatalogStatus | AmbulanceStatus;
}

export function CatalogStatusBadge({ estado }: CatalogStatusBadgeProps) {
  return <Badge tone={CATALOG_STATUS_TONE[estado]}>{estado}</Badge>;
}
