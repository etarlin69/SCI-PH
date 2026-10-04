import { useEffect, useState } from 'react';
import { Checkbox, ConfirmDialog } from '../../../components/shared';
import { plural } from '../format';

export interface RevokeAllSessionsDialogProps {
  open: boolean;
  userName: string;
  count: number;
  onCancel: () => void;
  onConfirm: (forcePasswordChange: boolean) => Promise<void>;
}

/** Confirmación de "Cerrar todas las sesiones", con la opción de forzar el cambio de contraseña. */
export function RevokeAllSessionsDialog({ open, userName, count, onCancel, onConfirm }: RevokeAllSessionsDialogProps) {
  const [force, setForce] = useState(true);

  useEffect(() => {
    if (open) setForce(true);
  }, [open]);

  return (
    <ConfirmDialog
      open={open}
      title="¿Cerrar todas las sesiones?"
      confirmLabel="Cerrar todas las sesiones"
      onCancel={onCancel}
      onConfirm={() => onConfirm(force)}
      message={
        <div className="flex flex-col gap-4">
          <p>
            Se cerrarán las {plural(count, 'sesión activa', 'sesiones activas')} de {userName}, también en dispositivos de ambulancia.
            Tendrá que volver a iniciar sesión en todos sus dispositivos.
          </p>
          <Checkbox
            variant="card"
            checked={force}
            onChange={(e) => setForce(e.target.checked)}
            label="Exigir cambio de contraseña en el próximo acceso"
            description="Recomendado si sospechas que la cuenta está comprometida."
          />
        </div>
      }
    />
  );
}
