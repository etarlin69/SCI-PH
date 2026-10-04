import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import type { Invitation } from '../../../types';
import { Button, FormError, Input, Modal, Textarea } from '../../../components/shared';
import { useSubmit } from '../../../hooks/useSubmit';
import { invitationsService } from '../../../services';
import { EMAIL_PATTERN, MESSAGE_MAX } from '../helpers';

const schema = z.object({
  email: z.string().trim().regex(EMAIL_PATTERN, 'Formato inválido. Ejemplo: nombre@dominio.com'),
  mensaje: z.string().max(MESSAGE_MAX, `El mensaje supera los ${MESSAGE_MAX} caracteres.`),
});
type FormValues = z.infer<typeof schema>;

export interface InvitePatientModalProps {
  open: boolean;
  onClose: () => void;
  onSent: (invitation: Invitation) => void;
}

/** Invitación de paciente: recibirá un enlace para activar su cuenta en la app de pacientes. */
export function InvitePatientModal({ open, onClose, onSent }: InvitePatientModalProps) {
  const submit = useSubmit();
  const {
    register,
    handleSubmit,
    reset,
    watch,
    setError,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { email: '', mensaje: '' } });

  useEffect(() => {
    if (open) {
      reset({ email: '', mensaje: '' });
      submit.reset();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);
  useEffect(() => {
    if (submit.fieldError) setError('email', { message: submit.fieldError.message });
  }, [submit.fieldError, setError]);

  const send = handleSubmit(async (v) => {
    const inv = await submit.run(() => invitationsService.invitePatient({ email: v.email, mensaje: v.mensaje }));
    if (inv) onSent(inv);
  });

  return (
    <Modal
      open={open}
      onClose={onClose}
      dismissible={!submit.submitting}
      title="Invitar paciente"
      description="Recibirá un enlace para activar su cuenta en la app de pacientes."
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submit.submitting}>
            Cancelar
          </Button>
          <Button type="submit" form="invite-patient" loading={submit.submitting} loadingText="Enviando…">
            Enviar invitación
          </Button>
        </>
      }
    >
      <form id="invite-patient" noValidate onSubmit={send} className="flex flex-col gap-4">
        {submit.error && <FormError message={`No se pudo enviar. ${submit.error} Los datos siguen en el formulario; vuelve a enviar.`} />}
        <Input
          label="Email"
          type="email"
          required
          hint="El que el paciente usará para iniciar sesión."
          error={errors.email?.message}
          {...register('email')}
        />
        <Textarea
          label="Mensaje personalizado (opcional)"
          placeholder="Ej.: Hola, te invitamos a consultar tus resultados en la app."
          hint="Se añade al email de invitación. No incluyas datos clínicos."
          maxLength={MESSAGE_MAX}
          showCount
          count={(watch('mensaje') ?? '').length}
          error={errors.mensaje?.message}
          {...register('mensaje')}
        />
        <p className="text-xs text-ink-muted">El enlace caduca en 7 días y solo se puede usar una vez.</p>
      </form>
    </Modal>
  );
}
