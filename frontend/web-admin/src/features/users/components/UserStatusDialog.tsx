import { ConfirmDialog } from '../../../components/shared';
import type { User } from '../../../types';

export interface UserStatusDialogProps {
  /** Usuario a suspender o reactivar (`null` = cerrado). */
  user: User | null;
  onConfirm: (user: User) => Promise<void>;
  onCancel: () => void;
}

/** Confirmación de suspender (cierra sus sesiones) o reactivar una cuenta. */
export function UserStatusDialog({ user, onConfirm, onCancel }: UserStatusDialogProps) {
  const suspend = user?.estado !== 'Suspendido';
  return (
    <ConfirmDialog
      open={!!user}
      tone={suspend ? 'danger' : 'primary'}
      title={suspend ? `¿Suspender a ${user?.nombre ?? ''}?` : `¿Reactivar a ${user?.nombre ?? ''}?`}
      message={
        suspend
          ? 'Perderá el acceso de inmediato y se cerrarán sus sesiones activas. Podrás reactivarlo más tarde. Queda registrado en auditoría.'
          : `Recuperará el acceso con su rol actual (${user?.rol ?? ''}). Queda registrado en auditoría.`
      }
      confirmLabel={suspend ? 'Suspender' : 'Reactivar'}
      onConfirm={() => (user ? onConfirm(user) : undefined)}
      onCancel={onCancel}
    />
  );
}
