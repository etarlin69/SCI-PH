import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import type { Role, RoleInput } from '../../../types';
import { Button, FormError, Input, Modal, Select, Textarea } from '../../../components/shared';
import { useSubmit } from '../../../hooks/useSubmit';

const DESC_MAX = 120;

const schema = z.object({
  nombre: z.string().trim().min(3, 'Escribe un nombre de al menos 3 caracteres.').max(40, 'Máximo 40 caracteres.'),
  descripcion: z.string().trim().max(DESC_MAX, `La descripción supera ${DESC_MAX} caracteres.`),
  copiarDe: z.string(),
});
type FormValues = z.infer<typeof schema>;

export interface RoleFormModalProps {
  open: boolean;
  role?: Role;
  roles: Role[];
  copyFromId?: string;
  onClose: () => void;
  onSubmit: (input: RoleInput) => Promise<Role>;
  onSaved: (role: Role) => void;
}

/** Crear rol personalizado (opcionalmente copiando permisos) o editar nombre/descripción. */
export function RoleFormModal({ open, role, roles, copyFromId, onClose, onSubmit, onSaved }: RoleFormModalProps) {
  const editing = !!role;
  const submit = useSubmit();
  const {
    register,
    handleSubmit,
    reset,
    watch,
    setError,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  useEffect(() => {
    if (!open) return;
    const source = roles.find((r) => r.id === copyFromId);
    reset({
      nombre: role?.nombre ?? (source ? `${source.nombre} (copia)` : ''),
      descripcion: role?.descripcion ?? '',
      copiarDe: copyFromId ?? '',
    });
    submit.reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, role, copyFromId]);

  useEffect(() => {
    if (submit.fieldError) setError(submit.fieldError.field as keyof FormValues, { message: submit.fieldError.message });
  }, [submit.fieldError, setError]);

  const descripcion = watch('descripcion') ?? '';

  const save = handleSubmit(async (v) => {
    const result = await submit.run(() => onSubmit({ nombre: v.nombre, descripcion: v.descripcion, copiarDe: v.copiarDe || undefined }));
    if (result) onSaved(result);
  });

  return (
    <Modal
      open={open}
      onClose={onClose}
      dismissible={!submit.submitting}
      title={editing ? `Editar rol «${role.nombre}»` : 'Nuevo rol personalizado'}
      description={editing ? 'Los permisos se cambian en la matriz.' : 'Los campos marcados con * son obligatorios.'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submit.submitting}>
            Cancelar
          </Button>
          <Button type="submit" form="role-form" loading={submit.submitting} loadingText="Guardando…">
            {editing ? 'Guardar cambios' : 'Crear rol'}
          </Button>
        </>
      }
    >
      <form id="role-form" noValidate onSubmit={save} className="flex flex-col gap-4">
        <FormError message={submit.error} />
        <Input
          label="Nombre del rol"
          required
          hint="Debe ser único. Ej.: Supervisor de turno."
          error={errors.nombre?.message}
          {...register('nombre')}
        />
        <Textarea
          label="Descripción"
          maxLength={DESC_MAX}
          showCount
          count={descripcion.length}
          error={errors.descripcion?.message}
          rows={3}
          {...register('descripcion')}
        />
        {!editing && (
          <Select
            label="Copiar permisos de"
            hint="Después podrás ajustar cada permiso en la matriz."
            options={[{ value: '', label: 'Ninguno (empezar sin permisos)' }, ...roles.map((r) => ({ value: r.id, label: r.nombre }))]}
            {...register('copiarDe')}
          />
        )}
      </form>
    </Modal>
  );
}
