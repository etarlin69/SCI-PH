import { Lock } from 'lucide-react';
import type { RoleType } from '../../../types';
import { Badge } from '../../../components/shared';

export interface RoleTypeBadgeProps {
  tipo: RoleType;
}

/** "Sistema" (solo lectura, con candado) o "Personalizado". */
export function RoleTypeBadge({ tipo }: RoleTypeBadgeProps) {
  return tipo === 'Sistema' ? (
    <Badge tone="neutral" dot={false}>
      <Lock size={11} aria-hidden="true" />
      Sistema
    </Badge>
  ) : (
    <Badge tone="accent" dot={false}>
      Personalizado
    </Badge>
  );
}
