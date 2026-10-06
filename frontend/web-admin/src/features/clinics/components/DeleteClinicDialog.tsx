import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Ban, Trash2 } from 'lucide-react';
import type { Clinic } from '../../../types';
import { clinicsService, errorMessage, isServiceError } from '../../../services';
import { Button, Modal, useToast } from '../../../components/shared';
import { cn } from '../../../utils/cn';

export interface DeleteClinicDialogProps {
  /** Clínica a eliminar; `null` = diálogo cerrado. */
  clinic: Clinic | null;
  canDeactivate: boolean;
  onClose: () => void;
  onDeleted: (clinic: Clinic) => void;
  onDeactivated: (clinic: Clinic) => void;
}

interface Usage {
  personal: number;
  dispositivos: number;
}

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/** Eliminar clínica. */
export function DeleteClinicDialog({ clinic, canDeactivate, onClose, onDeleted, onDeactivated }: DeleteClinicDialogProps) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [usage, setUsage] = useState<Usage | null>(null);

  useEffect(() => {
    setBusy(false);
    setError(null);
    setUsage(
      clinic && (clinic.personal > 0 || clinic.dispositivos > 0) ? { personal: clinic.personal, dispositivos: clinic.dispositivos } : null,
    );
  }, [clinic]);

  if (!clinic) return null;
  const blocked = usage !== null;
  const offerDeactivate = blocked && canDeactivate && clinic.estado === 'Activa';

  const remove = async () => {
    setBusy(true);
    setError(null);
    try {
      await clinicsService.remove(clinic.id);
      toast({ title: 'Clínica eliminada', description: `${clinic.nombre} se quitó del directorio.`, tone: 'success' });
      onDeleted(clinic);
    } catch (e) {
      if (isServiceError(e, 'IN_USE')) {
        setUsage({ personal: Number(e.details.personal ?? 0), dispositivos: Number(e.details.dispositivos ?? 0) });
      } else {
        setError(errorMessage(e));
      }
    } finally {
      setBusy(false);
    }
  };

  const deactivate = async () => {
    setBusy(true);
    setError(null);
    try {
      const updated = await clinicsService.setStatus(clinic.id, 'Inactiva');
      toast({ title: 'Clínica desactivada', description: `${clinic.nombre} ya no aparece como destino de traslados.`, tone: 'success' });
      onDeactivated(updated);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      role="alertdialog"
      size="sm"
      dismissible={!busy}
      closeOnBackdrop={false}
      title={blocked ? `No se puede eliminar «${clinic.nombre}»` : `¿Eliminar «${clinic.nombre}»?`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {blocked && !offerDeactivate ? 'Entendido' : 'Cancelar'}
          </Button>
          {!blocked && (
            <Button variant="danger" onClick={() => void remove()} loading={busy} loadingText="Procesando…">
              {error ? 'Reintentar' : 'Eliminar clínica'}
            </Button>
          )}
          {offerDeactivate && (
            <Button onClick={() => void deactivate()} loading={busy} loadingText="Procesando…">
              Desactivar clínica
            </Button>
          )}
        </>
      }
    >
      <div className="flex gap-3.5">
        <span
          className={cn(
            'flex h-10 w-10 shrink-0 items-center justify-center rounded-full',
            blocked ? 'bg-warning-50 text-warning-600' : 'bg-danger-50 text-danger-600',
          )}
        >
          {blocked ? <Ban size={20} aria-hidden="true" /> : <Trash2 size={20} aria-hidden="true" />}
        </span>
        <div className="flex min-w-0 flex-col gap-3 text-sm leading-relaxed text-ink-3">
          {blocked ? (
            <>
              <p>Tiene personal o dispositivos asignados.</p>
              <p className="rounded-lg bg-warning-50 px-3 py-2.5 text-[13px] text-warning-700">
                <strong className="font-semibold">{plural(usage.personal, 'persona', 'personas')}</strong> y{' '}
                <strong className="font-semibold">{plural(usage.dispositivos, 'dispositivo', 'dispositivos')}</strong> dependen de esta
                clínica. Reasígnalos en{' '}
                <Link to="/admin/personal" className="font-semibold underline">
                  Personal
                </Link>{' '}
                y{' '}
                <Link to="/admin/dispositivos" className="font-semibold underline">
                  Dispositivos
                </Link>{' '}
                antes de eliminarla.
              </p>
              <p className="text-[13px]">Si solo quieres dejar de recibir pacientes, desactívala: conservará su historial.</p>
            </>
          ) : (
            <p>
              Se eliminará del directorio de clínicas. Los traslados históricos la conservarán como referencia. Esta acción no se puede
              deshacer.
            </p>
          )}
          {error && (
            <p role="alert" className="rounded-lg bg-danger-50 px-3 py-2 text-[13px] text-danger-text">
              <strong className="font-semibold">No se pudo completar la acción.</strong> {error}
            </p>
          )}
        </div>
      </div>
    </Modal>
  );
}
