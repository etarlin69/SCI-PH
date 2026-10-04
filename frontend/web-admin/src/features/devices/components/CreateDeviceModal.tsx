import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Check } from 'lucide-react';
import type { Device, DeviceEnrollment, DeviceType } from '../../../types';
import { Button, Checkbox, ConfirmDialog, FormError, Input, Modal, Select } from '../../../components/shared';
import { useSubmit } from '../../../hooks/useSubmit';
import { devicesService } from '../../../services';
import { cn } from '../../../utils/cn';
import { DEVICE_TYPES } from '../helpers';
import { createDeviceSchema, type CreateDeviceFormValues } from '../schemas';
import { EnrollmentPanel } from './EnrollmentPanel';

export interface CreateDeviceModalProps {
  open: boolean;
  onClose: () => void;
  onCreated: (device: Device) => void;
  onDiscarded: () => void;
}

function Stepper({ step }: { step: 1 | 2 }) {
  const item = (n: 1 | 2, label: string) => (
    <li className="flex items-center gap-2" aria-current={step === n ? 'step' : undefined}>
      <span
        className={cn(
          'flex h-6 w-6 items-center justify-center rounded-full text-xs font-semibold',
          step > n ? 'bg-success-600 text-white' : step === n ? 'bg-primary-600 text-white' : 'bg-line text-ink-3',
        )}
      >
        {step > n ? <Check size={13} aria-hidden="true" /> : n}
      </span>
      <span className={cn('text-[13px]', step === n ? 'font-semibold text-ink' : 'text-ink-muted')}>{label}</span>
    </li>
  );
  return (
    <ol className="flex items-center gap-3" aria-label="Pasos">
      {item(1, 'Datos')}
      <span className="h-px w-8 bg-line" aria-hidden="true" />
      {item(2, 'Vincular')}
    </ol>
  );
}

/** Alta en 2 pasos: (1) nombre, tipo y asignación; (2) QR + secreto de un solo uso con caducidad. */
export function CreateDeviceModal({ open, onClose, onCreated, onDiscarded }: CreateDeviceModalProps) {
  const submit = useSubmit();
  const [enrollment, setEnrollment] = useState<DeviceEnrollment | null>(null);
  const [ack, setAck] = useState(false);
  const [ackError, setAckError] = useState(false);
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<CreateDeviceFormValues>({
    resolver: zodResolver(createDeviceSchema),
    defaultValues: { nombre: '', tipo: '', asignadoA: '' },
  });

  useEffect(() => {
    if (!open) return;
    reset({ nombre: '', tipo: '', asignadoA: '' });
    setEnrollment(null);
    setAck(false);
    setAckError(false);
    submit.reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  useEffect(() => {
    if (submit.fieldError) setError('nombre', { message: submit.fieldError.message });
  }, [submit.fieldError, setError]);

  const generate = handleSubmit(async (v) => {
    const result = await submit.run(() =>
      devicesService.create({ nombre: v.nombre, tipo: v.tipo as DeviceType, asignadoA: v.asignadoA || undefined }),
    );
    if (result) setEnrollment(result);
  });

  const requestClose = () => {
    if (enrollment) setConfirmDiscard(true);
    else onClose();
  };

  const discard = async () => {
    if (enrollment) await devicesService.discardEnrollment(enrollment.device.id).catch(() => undefined);
    setConfirmDiscard(false);
    setEnrollment(null);
    onDiscarded();
  };

  const finish = () => {
    if (!enrollment) return;
    if (!ack) {
      setAckError(true);
      return;
    }
    onCreated(enrollment.device);
    setEnrollment(null);
  };

  return (
    <>
      <Modal
        open={open}
        onClose={requestClose}
        dismissible={!submit.submitting}
        closeOnBackdrop={!enrollment}
        size="lg"
        title="Nuevo dispositivo"
        description={<Stepper step={enrollment ? 2 : 1} />}
        footer={
          enrollment ? (
            <>
              <Button variant="secondary" onClick={requestClose}>
                Cancelar
              </Button>
              <Button onClick={finish}>Confirmar</Button>
            </>
          ) : (
            <>
              <Button variant="secondary" onClick={onClose} disabled={submit.submitting}>
                Cancelar
              </Button>
              <Button type="submit" form="device-form" loading={submit.submitting} loadingText="Generando credencial…">
                Generar credencial
              </Button>
            </>
          )
        }
      >
        {enrollment ? (
          <div className="flex flex-col gap-5">
            <p className="text-sm text-ink-3">
              <strong className="text-ink">{enrollment.device.nombre}</strong> registrado como{' '}
              <span className="font-mono">{enrollment.device.id}</span>. Vincúlalo ahora.
            </p>
            <EnrollmentPanel
              enrollment={enrollment}
              expiredHint="El código caducó. Confirma el alta y rota la credencial desde el detalle del dispositivo para generar otro."
            />
            <div className="flex flex-col gap-1">
              <Checkbox
                label="He escaneado el código o guardado el secreto"
                checked={ack}
                onChange={(e) => {
                  setAck(e.target.checked);
                  setAckError(false);
                }}
              />
              {ackError && (
                <p role="alert" className="text-xs font-medium text-danger-text">
                  Confírmalo antes de continuar.
                </p>
              )}
            </div>
          </div>
        ) : (
          <form id="device-form" noValidate onSubmit={generate} className="flex flex-col gap-4">
            {submit.error && (
              <FormError message={`No se pudo registrar. ${submit.error} No se generó ningún secreto; vuelve a intentarlo.`} />
            )}
            <Input label="Nombre" required placeholder="Tablet AMB-23" error={errors.nombre?.message} {...register('nombre')} />
            <Select
              label="Tipo"
              required
              placeholder="Selecciona un tipo"
              hint="Tablet, móvil o puesto de escritorio."
              options={DEVICE_TYPES.map((t) => ({ value: t, label: t }))}
              error={errors.tipo?.message}
              {...register('tipo')}
            />
            <Input
              label="Asignado a (opcional)"
              hint="Unidad, clínica o puesto."
              error={errors.asignadoA?.message}
              {...register('asignadoA')}
            />
            <p className="rounded-lg bg-surface-subtle px-3 py-2.5 text-[13px] text-ink-3">
              En el siguiente paso se genera una credencial de un solo uso (QR y secreto). Ten el dispositivo a mano para vincularlo. El
              dispositivo aparecerá como «Pendiente» hasta su primera sincronización.
            </p>
          </form>
        )}
      </Modal>
      <ConfirmDialog
        open={confirmDiscard}
        title="¿Descartar el alta?"
        message="El dispositivo no se registrará y el secreto generado dejará de ser válido."
        confirmLabel="Descartar alta"
        cancelLabel="Seguir vinculando"
        onConfirm={discard}
        onCancel={() => setConfirmDiscard(false)}
      />
    </>
  );
}
