import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { LICENSE_TYPES } from '../../../data/mock';
import type { License, LicenseInput } from '../../../types';
import { Button, FormError, Input, Modal, Select } from '../../../components/shared';
import { useSubmit } from '../../../hooks/useSubmit';
import { todayISO } from '../../../utils/dates';

const schema = z.object({
  tipo: z.string().min(1, 'Selecciona la licencia o certificación.'),
  numero: z.string().trim().min(1, 'Escribe el número del certificado.'),
  vence: z.string().refine((v) => !v || v > todayISO(), 'La fecha de vencimiento debe ser futura.'),
});
type FormValues = z.infer<typeof schema>;

export interface LicenseModalProps {
  open: boolean;
  staffName: string;
  renewing?: License;
  onClose: () => void;
  onSubmit: (input: LicenseInput, replaceId?: string) => Promise<License>;
  onSaved: (license: License, renewed: boolean) => void;
}

/** Añadir licencia/certificación o registrar su renovación (nuevo número y vencimiento). */
export function LicenseModal({ open, staffName, renewing, onClose, onSubmit, onSaved }: LicenseModalProps) {
  const submit = useSubmit();
  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  useEffect(() => {
    if (open) {
      reset({ tipo: renewing?.tipo ?? '', numero: '', vence: '' });
      submit.reset();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, renewing]);
  useEffect(() => {
    if (submit.fieldError) setError(submit.fieldError.field as keyof FormValues, { message: submit.fieldError.message });
  }, [submit.fieldError, setError]);

  const save = handleSubmit(async (v) => {
    const lic = await submit.run(() => onSubmit({ tipo: v.tipo, numero: v.numero, vence: v.vence || undefined }, renewing?.id));
    if (lic) onSaved(lic, !!renewing);
  });

  return (
    <Modal
      open={open}
      onClose={onClose}
      dismissible={!submit.submitting}
      title={renewing ? 'Registrar renovación' : 'Añadir licencia'}
      description={`${staffName} · Los campos con * son obligatorios.`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submit.submitting}>
            Cancelar
          </Button>
          <Button type="submit" form="license-form" loading={submit.submitting} loadingText="Guardando…">
            {renewing ? 'Registrar renovación' : 'Añadir licencia'}
          </Button>
        </>
      }
    >
      <form id="license-form" noValidate onSubmit={save} className="flex flex-col gap-4">
        <FormError message={submit.error && `No se pudo guardar. ${submit.error}`} />
        <Select
          label="Licencia o certificación"
          required
          placeholder="Selecciona"
          disabled={!!renewing}
          options={LICENSE_TYPES.map((t) => ({ value: t, label: t }))}
          error={errors.tipo?.message}
          {...register('tipo')}
        />
        <Input label="Número de certificado" required autoComplete="off" error={errors.numero?.message} {...register('numero')} />
        <Input
          label={renewing ? 'Nuevo vencimiento' : 'Vence el'}
          type="date"
          min={todayISO()}
          hint="Déjalo vacío si no vence. Se avisará 30 días antes del vencimiento."
          error={errors.vence?.message}
          {...register('vence')}
        />
      </form>
    </Modal>
  );
}
