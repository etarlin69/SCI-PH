import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { DEPARTAMENTOS } from '../../../data/mock';
import type { Invitation, Role } from '../../../types';
import { Button, FormError, Input, Modal, Select, Textarea } from '../../../components/shared';
import { useSubmit } from '../../../hooks/useSubmit';
import { invitationsService } from '../../../services';
import { EMAIL_PATTERN, MESSAGE_MAX } from '../helpers';

const schema = z.object({
  email: z.string().trim().regex(EMAIL_PATTERN, 'Formato inválido. Ejemplo: nombre@dominio.com'),
  rol: z.string().min(1, 'Selecciona un rol.'),
  departamento: z.string().min(1, 'Selecciona un departamento.'),
  mensaje: z.string().max(MESSAGE_MAX, `El mensaje supera los ${MESSAGE_MAX} caracteres.`),
});
type FormValues = z.infer<typeof schema>;

export interface InviteStaffModalProps {
  open: boolean;
  roles: Role[];
  onClose: () => void;
  onSent: (invitation: Invitation) => void;
}

/** Invitación de personal: crea su cuenta con el rol indicado al aceptar (y deberá configurar MFA). */
export function InviteStaffModal({ open, roles, onClose, onSent }: InviteStaffModalProps) {
  const submit = useSubmit();
  const {
    register,
    handleSubmit,
    reset,
    watch,
    setError,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { email: '', rol: '', departamento: '', mensaje: '' } });

  useEffect(() => {
    if (open) {
      reset({ email: '', rol: '', departamento: '', mensaje: '' });
      submit.reset();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);
  useEffect(() => {
    if (submit.fieldError) setError('email', { message: submit.fieldError.message });
  }, [submit.fieldError, setError]);

  const send = handleSubmit(async (v) => {
    const inv = await submit.run(() => invitationsService.inviteStaff(v));
    if (inv) onSent(inv);
  });

  return (
    <Modal
      open={open}
      onClose={onClose}
      dismissible={!submit.submitting}
      title="Invitar personal"
      description="Recibirá un enlace para crear su cuenta con el rol indicado."
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submit.submitting}>
            Cancelar
          </Button>
          <Button type="submit" form="invite-staff" loading={submit.submitting} loadingText="Enviando…">
            Enviar invitación
          </Button>
        </>
      }
    >
      <form id="invite-staff" noValidate onSubmit={send} className="flex flex-col gap-4">
        {submit.error && <FormError message={`No se pudo enviar. ${submit.error} Los datos siguen en el formulario; vuelve a enviar.`} />}
        <Input
          label="Email"
          type="email"
          required
          hint="Preferiblemente el email institucional."
          error={errors.email?.message}
          {...register('email')}
        />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Select
            label="Rol"
            required
            placeholder="Selecciona un rol"
            options={roles.map((r) => ({ value: r.nombre, label: r.nombre }))}
            error={errors.rol?.message}
            {...register('rol')}
          />
          <Select
            label="Departamento"
            required
            placeholder="Selecciona un departamento"
            hint="Se usará para su perfil de personal."
            options={DEPARTAMENTOS.map((d) => ({ value: d, label: d }))}
            error={errors.departamento?.message}
            {...register('departamento')}
          />
        </div>
        <Textarea
          label="Mensaje personalizado (opcional)"
          placeholder="Ej.: Bienvenido al equipo de Emergencias."
          hint="Se añade al email de invitación. No incluyas datos clínicos."
          maxLength={MESSAGE_MAX}
          showCount
          count={(watch('mensaje') ?? '').length}
          error={errors.mensaje?.message}
          {...register('mensaje')}
        />
        <p className="text-xs text-ink-muted">
          El enlace caduca en 7 días. Al aceptarlo, la cuenta aparecerá en Usuarios con el rol elegido y deberá configurar MFA.
        </p>
      </form>
    </Modal>
  );
}
