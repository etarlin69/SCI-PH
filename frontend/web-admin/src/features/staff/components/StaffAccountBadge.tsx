import { KeyRound, MailQuestion, MinusCircle } from 'lucide-react';
import type { StaffAccount } from '../../../types';

export interface StaffAccountBadgeProps {
  cuenta: StaffAccount;
}

/** Estado de la cuenta de acceso vinculada al perfil laboral. */
export function StaffAccountBadge({ cuenta }: StaffAccountBadgeProps) {
  if (cuenta.estado === 'vinculada') {
    return (
      <span className="inline-flex items-center gap-1 text-xs text-success-600">
        <KeyRound size={12} aria-hidden="true" /> Cuenta {cuenta.userId}
      </span>
    );
  }
  if (cuenta.estado === 'pendiente') {
    return (
      <span className="inline-flex items-center gap-1 text-xs text-warning-700">
        <MailQuestion size={12} aria-hidden="true" /> Invitación pendiente
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 text-xs text-ink-muted">
      <MinusCircle size={12} aria-hidden="true" /> Sin acceso
    </span>
  );
}
