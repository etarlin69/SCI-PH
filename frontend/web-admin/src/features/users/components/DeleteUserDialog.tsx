import { useState } from 'react';
import { ShieldCheck } from 'lucide-react';
import { Button, Modal } from '../../../components/shared';
import type { User } from '../../../types';

export interface DeleteUserDialogProps {
  user: User | null;
  onConfirm: (user: User) => Promise<void>;
  onSuspendInstead: (user: User) => void;
  onCancel: () => void;
}

/** Eliminación de cuenta: explica que el historial de auditoría se conserva y ofrece suspender en su lugar. */
export function DeleteUserDialog({ user, onConfirm, onSuspendInstead, onCancel }: DeleteUserDialogProps) {
  const [busy, setBusy] = useState(false);
  const confirm = async () => {
    if (!user) return;
    setBusy(true);
    try {
      await onConfirm(user);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal
      open={!!user}
      onClose={onCancel}
      dismissible={!busy}
      role="alertdialog"
      size="sm"
      title={`¿Eliminar la cuenta de ${user?.nombre ?? ''}?`}
      description="Perderá el acceso de inmediato y se cerrarán sus sesiones. Esta acción no se puede deshacer."
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            Cancelar
          </Button>
          <Button variant="danger" onClick={confirm} loading={busy} loadingText="Eliminando…">
            Eliminar cuenta
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 text-[13px] text-ink-3">
        <p className="flex gap-2 rounded-lg bg-surface-subtle px-3 py-2.5">
          <ShieldCheck size={16} className="mt-0.5 shrink-0 text-success-600" aria-hidden="true" />
          <span>
            <strong className="text-ink">Se conserva:</strong> todo su historial en Auditoría (registro inalterable).
          </span>
        </p>
        {user?.estado !== 'Suspendido' && (
          <p>
            ¿Solo quieres bloquear el acceso por un tiempo?{' '}
            <button
              type="button"
              className="font-semibold text-primary-600 hover:underline"
              onClick={() => user && onSuspendInstead(user)}
              disabled={busy}
            >
              Suspender en su lugar
            </button>
          </p>
        )}
      </div>
    </Modal>
  );
}
