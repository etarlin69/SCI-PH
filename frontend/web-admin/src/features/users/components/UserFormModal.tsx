import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import type { CreateUserInput, PasswordDelivery, Role, UpdateUserInput, User } from '../../../types';
import { Button, Checkbox, FormError, Input, Modal, Select } from '../../../components/shared';
import { useSubmit } from '../../../hooks/useSubmit';
import { generateTemporaryPassword } from '../../../services';
import { cn } from '../../../utils/cn';
import { TemporaryPasswordField } from './TemporaryPasswordField';

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
const ADMIN_ROLE = 'Administrador';

const schema = z.object({
  nombre: z.string().trim().min(3, 'Escribe el nombre completo (mínimo 3 caracteres).'),
  email: z.string().trim().regex(EMAIL, 'Formato de email inválido. Ejemplo: nombre@scih.com'),
  rol: z.string().min(1, 'Selecciona un rol.'),
  entrega: z.enum(['email', 'manual']),
  exigirMfa: z.boolean(),
});
type FormValues = z.infer<typeof schema>;

export interface UserFormModalProps {
  open: boolean;
  user?: User;
  roles: Role[];
  emailEditable?: boolean;
  onClose: () => void;
  onCreate: (input: CreateUserInput) => Promise<User>;
  onUpdate: (id: string, input: UpdateUserInput) => Promise<User>;
  onSaved: (user: User, created: boolean, delivery?: PasswordDelivery) => void;
}

/** Alta y edición de usuarios. */
export function UserFormModal({ open, user, roles, emailEditable = true, onClose, onCreate, onUpdate, onSaved }: UserFormModalProps) {
  const editing = !!user;
  const submit = useSubmit();
  const [tempPassword, setTempPassword] = useState('');
  const [copied, setCopied] = useState(false);
  const [copyError, setCopyError] = useState<string>();

  const {
    register,
    handleSubmit,
    reset,
    watch,
    control,
    setError,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { nombre: '', email: '', rol: '', entrega: 'email', exigirMfa: true },
  });

  useEffect(() => {
    if (!open) return;
    reset({ nombre: user?.nombre ?? '', email: user?.email ?? '', rol: user?.rol ?? '', entrega: 'email', exigirMfa: true });
    setTempPassword(editing ? '' : generateTemporaryPassword());
    setCopied(false);
    setCopyError(undefined);
    submit.reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, user]);

  useEffect(() => {
    if (submit.fieldError) setError(submit.fieldError.field as keyof FormValues, { message: submit.fieldError.message });
  }, [submit.fieldError, setError]);

  const entrega = watch('entrega');
  const rol = watch('rol');
  const email = watch('email');
  const mfaForced = rol === ADMIN_ROLE;

  const save = handleSubmit(async (v) => {
    if (!editing && v.entrega === 'manual' && !copied) {
      setCopyError('Copia la contraseña antes de guardar. No podrás volver a verla.');
      return;
    }
    const result = await submit.run(() =>
      editing
        ? onUpdate(user.id, { nombre: v.nombre, email: emailEditable ? v.email : user.email, rol: v.rol })
        : onCreate({
            nombre: v.nombre,
            email: v.email,
            rol: v.rol,
            entrega: v.entrega,
            exigirMfa: mfaForced || v.exigirMfa,
            temporaryPassword: tempPassword,
          }),
    );
    if (result) {
      setTempPassword('');
      onSaved(result, !editing, editing ? undefined : v.entrega);
    }
  });

  const roleOptions = roles.map((r) => ({ value: r.nombre, label: r.nombre }));

  return (
    <Modal
      open={open}
      onClose={onClose}
      dismissible={!submit.submitting}
      size="md"
      title={editing ? 'Editar usuario' : 'Nuevo usuario'}
      description={editing ? `${user.id} · ${user.email}` : 'Los campos marcados con * son obligatorios.'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submit.submitting}>
            Cancelar
          </Button>
          <Button type="submit" form="user-form" loading={submit.submitting} loadingText="Guardando…">
            {editing ? 'Guardar cambios' : 'Crear usuario'}
          </Button>
        </>
      }
    >
      <form id="user-form" noValidate onSubmit={save} className="flex flex-col gap-4">
        {submit.error && <FormError message={`No se pudo guardar. ${submit.error}`} />}
        <Input
          label="Nombre completo"
          required
          hint="Tal como aparecerá en el sistema."
          autoComplete="off"
          error={errors.nombre?.message}
          {...register('nombre')}
        />
        {emailEditable ? (
          <Input
            label="Email"
            type="email"
            required
            hint="Se usará para iniciar sesión."
            autoComplete="off"
            error={errors.email?.message}
            {...register('email')}
          />
        ) : (
          <p className="text-[13px] text-ink-muted">
            Email: <strong className="text-ink">{user?.email}</strong> (se edita desde la lista de usuarios)
          </p>
        )}
        <Select
          label="Rol"
          required
          placeholder="Selecciona un rol"
          options={roleOptions}
          error={errors.rol?.message}
          hint={editing ? 'El cambio de rol se aplica en su próximo inicio de sesión.' : undefined}
          {...register('rol')}
        />

        {!editing && (
          <fieldset className="flex flex-col gap-4 rounded-lg border border-line p-4">
            <legend className="px-1 text-[13px] font-semibold text-ink-2">Acceso inicial</legend>
            <TemporaryPasswordField
              password={tempPassword}
              copied={copied}
              error={copyError}
              onCopied={() => {
                setCopied(true);
                setCopyError(undefined);
              }}
              onRegenerate={() => {
                setTempPassword(generateTemporaryPassword());
                setCopied(false);
              }}
            />
            <Controller
              control={control}
              name="entrega"
              render={({ field }) => (
                <div role="radiogroup" aria-label="Entrega de la contraseña" className="flex flex-col gap-2">
                  <span className="text-[13px] font-semibold text-ink-2">Entrega de la contraseña</span>
                  {(
                    [
                      [
                        'email',
                        'Enviar por email al usuario',
                        email && EMAIL.test(email)
                          ? `Se enviará un enlace de un solo uso a ${email}.`
                          : 'Se enviará un enlace de un solo uso al email indicado.',
                      ],
                      ['manual', 'La entregaré yo por un canal seguro', 'Copia la contraseña antes de guardar. No podrás volver a verla.'],
                    ] as const
                  ).map(([value, label, hint]) => (
                    <label
                      key={value}
                      className={cn(
                        'flex cursor-pointer gap-3 rounded-lg border px-3 py-2.5',
                        field.value === value ? 'border-primary-600 bg-primary-50' : 'border-line hover:bg-surface-subtle',
                      )}
                    >
                      <input
                        type="radio"
                        name={field.name}
                        value={value}
                        checked={field.value === value}
                        onChange={() => field.onChange(value)}
                        className="mt-1 accent-primary-600"
                      />
                      <span className="flex flex-col">
                        <span className="text-sm font-medium text-ink">
                          {label}
                          {value === 'email' && <span className="font-normal text-ink-muted"> · recomendado</span>}
                        </span>
                        <span className="text-xs text-ink-muted">{hint}</span>
                      </span>
                    </label>
                  ))}
                </div>
              )}
            />
            {entrega === 'manual' && !copied && (
              <p className="text-xs font-medium text-warning-700">Copia la contraseña antes de guardar.</p>
            )}
            <Checkbox
              label="Exigir configurar MFA (código TOTP) en el primer acceso"
              description={
                mfaForced ? 'Obligatorio para el rol Administrador.' : 'Recomendado. Podrá configurarlo con cualquier app de autenticación.'
              }
              checked={mfaForced || !!watch('exigirMfa')}
              disabled={mfaForced}
              {...register('exigirMfa')}
            />
          </fieldset>
        )}

        {editing && (
          <p className="text-xs text-ink-muted">
            El cambio de rol queda en auditoría. Para suspender la cuenta o cerrar sus sesiones usa las acciones de la tabla o{' '}
            <Link to={`/admin/usuarios/${user.id}`} className="font-semibold text-primary-600 hover:underline" onClick={onClose}>
              Ver detalles
            </Link>
            .
          </p>
        )}
      </form>
    </Modal>
  );
}
