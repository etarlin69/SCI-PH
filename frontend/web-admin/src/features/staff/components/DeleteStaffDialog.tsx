import { useEffect, useState } from 'react';
import type { StaffMember } from '../../../types';
import { Button, Checkbox, Modal } from '../../../components/shared';

export interface DeleteStaffDialogProps {
  member: StaffMember | null;
  onConfirm: (member: StaffMember, suspendAccount: boolean) => Promise<void>;
  onDeactivateInstead?: (member: StaffMember) => void;
  onCancel: () => void;
}

/** Eliminar perfil laboral, con la opción (recomendada) de suspender también su cuenta de usuario. */
export function DeleteStaffDialog({ member, onConfirm, onDeactivateInstead, onCancel }: DeleteStaffDialogProps) {
  const [suspend, setSuspend] = useState(true);
  const [busy, setBusy] = useState(false);
  useEffect(() => setSuspend(true), [member]);
  if (!member) return null;
  const linked = member.cuenta.estado === 'vinculada' ? member.cuenta.userId : null;

  const confirm = async () => {
    setBusy(true);
    try {
      await onConfirm(member, !!linked && suspend);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      onClose={onCancel}
      dismissible={!busy}
      role="alertdialog"
      size="sm"
      title={`¿Eliminar a ${member.nombre}?`}
      description="Se eliminarán su perfil de personal, asignaciones y licencias. Los registros clínicos que firmó y su historial en Auditoría se conservan. Esta acción no se puede deshacer."
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            Cancelar
          </Button>
          <Button variant="danger" onClick={confirm} loading={busy} loadingText="Eliminando…">
            Eliminar perfil
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 text-[13px] text-ink-3">
        {linked && (
          <Checkbox
            label={`Suspender también su cuenta de usuario (${linked})`}
            description="Recomendado: si no, podrá seguir iniciando sesión."
            checked={suspend}
            onChange={(e) => setSuspend(e.target.checked)}
          />
        )}
        {onDeactivateInstead && member.estado !== 'Inactivo' && (
          <p>
            ¿Solo ha dejado la clínica temporalmente?{' '}
            <button
              type="button"
              className="font-semibold text-primary-600 hover:underline"
              disabled={busy}
              onClick={() => onDeactivateInstead(member)}
            >
              Marcar como inactivo
            </button>
          </p>
        )}
      </div>
    </Modal>
  );
}
