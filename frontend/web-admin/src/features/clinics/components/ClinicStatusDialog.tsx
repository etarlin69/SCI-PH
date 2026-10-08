import { useEffect, useState } from 'react';
import type { Clinic } from '../../../types';
import { clinicsService, errorMessage } from '../../../services';
import { ConfirmDialog, useToast } from '../../../components/shared';

export interface ClinicStatusDialogProps {
  /** Clínica cuyo estado se va a cambiar; `null` = diálogo cerrado. */
  clinic: Clinic | null;
  onClose: () => void;
  onChanged: (clinic: Clinic) => void;
}

/** Confirmación para desactivar (deja de ser destino de traslados) o reactivar una clínica. */
export function ClinicStatusDialog({ clinic, onClose, onChanged }: ClinicStatusDialogProps) {
  const toast = useToast();
  const [error, setError] = useState<string | null>(null);
  useEffect(() => setError(null), [clinic]);

  if (!clinic) return null;
  const deactivate = clinic.estado === 'Activa';

  const confirm = async () => {
    setError(null);
    try {
      const updated = await clinicsService.setStatus(clinic.id, deactivate ? 'Inactiva' : 'Activa');
      toast({
        title: deactivate ? 'Clínica desactivada' : 'Clínica reactivada',
        description: deactivate
          ? `${clinic.nombre} ya no aparece como destino de traslados.`
          : `${clinic.nombre} vuelve a aparecer como destino de traslados.`,
        tone: 'success',
      });
      onChanged(updated);
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <ConfirmDialog
      open
      tone={deactivate ? 'danger' : 'primary'}
      title={deactivate ? `¿Desactivar ${clinic.nombre}?` : `¿Reactivar ${clinic.nombre}?`}
      confirmLabel={error ? 'Reintentar' : deactivate ? 'Desactivar clínica' : 'Reactivar'}
      onCancel={onClose}
      onConfirm={confirm}
      message={
        <div className="flex flex-col gap-3">
          <p>
            {deactivate
              ? 'Dejará de aparecer como destino de traslados. Su personal y dispositivos siguen asignados y podrás reactivarla en cualquier momento.'
              : 'Volverá a aparecer como destino de traslados.'}
          </p>
          {error && (
            <p role="alert" className="rounded-lg bg-danger-50 px-3 py-2 text-[13px] text-danger-text">
              <strong className="font-semibold">No se pudo guardar.</strong> {error}
            </p>
          )}
        </div>
      }
    />
  );
}
