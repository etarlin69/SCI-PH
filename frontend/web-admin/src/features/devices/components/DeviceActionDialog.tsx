import { useEffect, useState } from 'react';
import { AlertTriangle } from 'lucide-react';
import type { Device } from '../../../types';
import { Button, Checkbox, FormError, Modal } from '../../../components/shared';
import { useSubmit } from '../../../hooks/useSubmit';
import { relativeTime, type DeviceAction } from '../helpers';

export interface DeviceActionDialogProps {
  /** Acción a confirmar; `null` = cerrado. */
  action: { kind: DeviceAction; device: Device } | null;
  onCancel: () => void;
  onConfirm: (kind: DeviceAction, device: Device) => Promise<void>;
}

interface Copy {
  title: string;
  body: string;
  warn?: string;
  ack?: string;
  cta: string;
  tone: 'danger' | 'primary';
}

function copyFor(kind: DeviceAction, d: Device): Copy {
  if (kind === 'rotate') {
    return {
      title: `¿Rotar la credencial de ${d.nombre}?`,
      body: 'Se generará una credencial nueva y la actual dejará de funcionar en cuanto confirmes. Tendrás que volver a vincular el dispositivo con el QR.',
      warn: 'Si la ambulancia está en servicio, hazlo cuando vuelva a base: hasta revincularla no podrá sincronizar.',
      cta: 'Genera una credencial nueva',
      tone: 'primary',
    };
  }
  if (kind === 'revoke') {
    return {
      title: `¿Revocar ${d.nombre}?`,
      body: 'La credencial deja de funcionar de inmediato y el dispositivo no podrá volver a sincronizar.',
      warn: 'Los registros que la tablet no haya enviado se quedarán en el equipo. No se puede deshacer: para reutilizarla habrá que registrarla de nuevo.',
      ack: 'Entiendo que la revocación es inmediata e irreversible.',
      cta: 'Revocar credencial',
      tone: 'danger',
    };
  }
  const active = d.estado === 'Activo';
  return {
    title: `¿Eliminar ${d.nombre}?`,
    body: active
      ? 'El dispositivo está activo: se revocará su credencial y se eliminará del inventario. Su historial se conserva en Auditoría.'
      : 'Se eliminará del inventario. Su historial se conserva en Auditoría. Esta acción no se puede deshacer.',
    warn: active && d.ultimoAcceso ? `Está activo (último acceso ${relativeTime(d.ultimoAcceso).toLowerCase()}).` : undefined,
    ack: active ? 'Entiendo que el equipo está activo y dejará de sincronizar al eliminarlo.' : undefined,
    cta: 'Eliminar dispositivo',
    tone: 'danger',
  };
}

/** Confirmación de rotar / revocar / eliminar, con casilla de reconocimiento en las irreversibles. */
export function DeviceActionDialog({ action, onCancel, onConfirm }: DeviceActionDialogProps) {
  const [ack, setAck] = useState(false);
  const submit = useSubmit();
  useEffect(() => {
    setAck(false);
    submit.reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [action]);

  if (!action) return null;
  const c = copyFor(action.kind, action.device);
  const run = () => submit.run(() => onConfirm(action.kind, action.device));

  return (
    <Modal
      open
      onClose={onCancel}
      dismissible={!submit.submitting}
      role="alertdialog"
      size="sm"
      title={c.title}
      description={c.body}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={submit.submitting}>
            Cancelar
          </Button>
          <Button
            variant={c.tone === 'danger' ? 'danger' : 'primary'}
            onClick={run}
            disabled={!!c.ack && !ack}
            loading={submit.submitting}
            loadingText="Procesando…"
          >
            {c.cta}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        {submit.error && <FormError message={submit.error} />}
        {c.warn && (
          <p className="flex gap-2 rounded-lg bg-warning-50 px-3 py-2.5 text-[13px] text-warning-700">
            <AlertTriangle size={16} className="mt-0.5 shrink-0" aria-hidden="true" />
            {c.warn}
          </p>
        )}
        {c.ack && <Checkbox label={c.ack} checked={ack} onChange={(e) => setAck(e.target.checked)} />}
      </div>
    </Modal>
  );
}
