import { ConfirmDialog } from '../../../components/shared';
import type { Invitation } from '../../../types';

export type InvitationActionKind = 'resend' | 'revoke';

export interface InvitationActionDialogProps {
  action: { kind: InvitationActionKind; invitation: Invitation } | null;
  onConfirm: (kind: InvitationActionKind, invitation: Invitation) => Promise<void>;
  onCancel: () => void;
}

/** Confirmación de reenviar (enlace nuevo, reinicia 7 días) o revocar (el enlace deja de funcionar). */
export function InvitationActionDialog({ action, onConfirm, onCancel }: InvitationActionDialogProps) {
  const resend = action?.kind === 'resend';
  const email = action?.invitation.email ?? '';
  return (
    <ConfirmDialog
      open={!!action}
      tone={resend ? 'primary' : 'danger'}
      title={resend ? '¿Reenviar la invitación?' : `¿Revocar la invitación a ${email}?`}
      message={
        resend
          ? `Se enviará un enlace nuevo a ${email} y el anterior dejará de funcionar. La caducidad se reinicia a 7 días.`
          : 'El enlace dejará de funcionar de inmediato. Podrás enviar una invitación nueva más adelante. Queda registrado en auditoría.'
      }
      confirmLabel={resend ? 'Reenviar' : 'Revocar invitación'}
      onConfirm={() => (action ? onConfirm(action.kind, action.invitation) : undefined)}
      onCancel={onCancel}
    />
  );
}
