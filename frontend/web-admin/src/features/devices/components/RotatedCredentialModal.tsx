import { useEffect, useState } from 'react';
import type { DeviceEnrollment } from '../../../types';
import { Button, Checkbox, Modal } from '../../../components/shared';
import { EnrollmentPanel } from './EnrollmentPanel';

export interface RotatedCredentialModalProps {
  /** Nueva credencial tras rotar; `null` = cerrado. */
  enrollment: DeviceEnrollment | null;
  onDone: () => void;
}

/** "Nueva credencial generada": QR + secreto de un solo uso para revincular el dispositivo. */
export function RotatedCredentialModal({ enrollment, onDone }: RotatedCredentialModalProps) {
  const [ack, setAck] = useState(false);
  useEffect(() => setAck(false), [enrollment]);
  if (!enrollment) return null;
  return (
    <Modal
      open
      onClose={() => ack && onDone()}
      dismissible={ack}
      closeOnBackdrop={false}
      size="lg"
      title="Nueva credencial generada"
      description={`La credencial anterior ya no funciona. Vuelve a vincular ${enrollment.device.nombre}.`}
      footer={
        <Button onClick={onDone} disabled={!ack}>
          Hecho
        </Button>
      }
    >
      <div className="flex flex-col gap-5">
        <EnrollmentPanel enrollment={enrollment} expiredHint="Código caducado: rota la credencial de nuevo para generar otro." />
        <Checkbox label="He vinculado el dispositivo o guardado el secreto" checked={ack} onChange={(e) => setAck(e.target.checked)} />
      </div>
    </Modal>
  );
}
