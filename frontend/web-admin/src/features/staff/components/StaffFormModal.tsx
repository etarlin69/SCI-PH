import { useEffect, useRef } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { DEPARTAMENTOS, PUESTOS, ROL_SUGERIDO } from '../../../data/mock';
import type { Clinic, Departamento, Puesto, Role, StaffAccessInput, StaffInput, StaffMember } from '../../../types';
import { Button, FormError, Input, Modal, Select } from '../../../components/shared';
import { useSubmit } from '../../../hooks/useSubmit';
import { cn } from '../../../utils/cn';
import { STAFF_STATUSES, STATUS_HINT } from '../filters';

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

const schema = z
  .object({
    nombre: z.string().trim().min(3, 'Escribe el nombre completo (mínimo 3 caracteres).'),
    email: z.string().trim().regex(EMAIL, 'Formato inválido. Ejemplo: nombre@scih.com'),
    telefono: z
      .string()
      .trim()
      .refine((v) => !v || /^[\d\s()+-]{7,20}$/.test(v), 'Teléfono no válido. Ejemplo: 809-555-0142'),
    puesto: z.string().min(1, 'Selecciona un puesto.'),
    clinicId: z.string().min(1, 'Selecciona una clínica.'),
    departamento: z.string().min(1, 'Selecciona el departamento.'),
    estado: z.enum(['Activo', 'De licencia', 'Inactivo']),
    invitar: z.boolean(),
    rol: z.string(),
  })
  .refine((v) => !v.invitar || !!v.rol, { path: ['rol'], message: 'Elige el rol de su cuenta.' });
type FormValues = z.infer<typeof schema>;

export interface StaffFormModalProps {
  open: boolean;
  member?: StaffMember;
  clinics: Pick<Clinic, 'id' | 'nombre' | 'estado'>[];
  roles: Role[];
  accountText?: string;
  onClose: () => void;
  onSubmit: (input: StaffInput, access: StaffAccessInput) => Promise<StaffMember>;
  onSaved: (member: StaffMember, created: boolean, invited: boolean) => void;
}

/** Alta/edición del perfil laboral. */
export function StaffFormModal({ open, member, clinics, roles, accountText, onClose, onSubmit, onSaved }: StaffFormModalProps) {
  const editing = !!member;
  const submit = useSubmit();
  const lastSuggested = useRef('');
  const {
    register,
    handleSubmit,
    reset,
    watch,
    control,
    setValue,
    getValues,
    setError,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  useEffect(() => {
    if (!open) return;
    lastSuggested.current = '';
    reset({
      nombre: member?.nombre ?? '',
      email: member?.email ?? '',
      telefono: member?.telefono ?? '',
      puesto: member?.puesto ?? '',
      clinicId: member?.clinicId ?? '',
      departamento: member?.departamento ?? '',
      estado: member?.estado ?? 'Activo',
      invitar: true,
      rol: '',
    });
    submit.reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, member]);

  useEffect(() => {
    if (submit.fieldError) setError(submit.fieldError.field as keyof FormValues, { message: submit.fieldError.message });
  }, [submit.fieldError, setError]);

  // Sugiere el rol según el puesto, salvo que el usuario ya haya elegido otro.
  const puesto = watch('puesto') as Puesto | '';
  useEffect(() => {
    if (!puesto || editing) return;
    const suggested = ROL_SUGERIDO[puesto];
    const current = getValues('rol');
    if (!current || current === lastSuggested.current) setValue('rol', suggested);
    lastSuggested.current = suggested;
  }, [puesto, editing, getValues, setValue]);

  const invitar = watch('invitar');

  const save = handleSubmit(async (v) => {
    const input: StaffInput = {
      nombre: v.nombre,
      email: v.email,
      telefono: v.telefono || undefined,
      puesto: v.puesto as Puesto,
      clinicId: v.clinicId,
      departamento: v.departamento as Departamento,
      estado: v.estado,
    };
    const access: StaffAccessInput = { invitar: !editing && v.invitar, rol: v.invitar ? v.rol : undefined };
    const result = await submit.run(() => onSubmit(input, access));
    if (result) onSaved(result, !editing, access.invitar);
  });

  const clinicOptions = clinics
    .filter((c) => c.estado === 'Activa' || c.id === member?.clinicId)
    .map((c) => ({ value: c.id, label: c.estado === 'Activa' ? c.nombre : `${c.nombre} (inactiva)` }));

  return (
    <Modal
      open={open}
      onClose={onClose}
      dismissible={!submit.submitting}
      size="lg"
      title={editing ? `Editar a ${member.nombre}` : 'Nuevo personal'}
      description={editing ? `${member.id} · perfil laboral` : 'Los campos con * son obligatorios.'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submit.submitting}>
            Cancelar
          </Button>
          <Button type="submit" form="staff-form" loading={submit.submitting} loadingText="Guardando…">
            {editing ? 'Guardar cambios' : invitar ? 'Guardar e invitar' : 'Guardar'}
          </Button>
        </>
      }
    >
      <form id="staff-form" noValidate onSubmit={save} className="flex flex-col gap-4">
        {submit.error && <FormError message={`No se pudo guardar. ${submit.error} Tus datos siguen en el formulario.`} />}
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input
            label="Nombre completo"
            required
            error={errors.nombre?.message}
            containerClassName="sm:col-span-2"
            {...register('nombre')}
          />
          <Input
            label="Email"
            type="email"
            required
            hint="Se usará también para su cuenta."
            error={errors.email?.message}
            {...register('email')}
          />
          <Input label="Teléfono" type="tel" hint="Opcional" error={errors.telefono?.message} {...register('telefono')} />
          <Select
            label="Puesto"
            required
            placeholder="Selecciona un puesto"
            options={PUESTOS.map((p) => ({ value: p, label: p }))}
            error={errors.puesto?.message}
            {...register('puesto')}
          />
          <Select
            label="Clínica"
            required
            placeholder="Selecciona una clínica"
            options={clinicOptions}
            error={errors.clinicId?.message}
            {...register('clinicId')}
          />
          <Select
            label="Departamento"
            required
            placeholder="Selecciona un departamento"
            options={DEPARTAMENTOS.map((d) => ({ value: d, label: d }))}
            error={errors.departamento?.message}
            containerClassName="sm:col-span-2"
            {...register('departamento')}
          />
        </div>

        <Controller
          control={control}
          name="estado"
          render={({ field }) => (
            <div role="radiogroup" aria-label="Estado" className="flex flex-col gap-2">
              <span className="text-[13px] font-semibold text-ink-2">Estado</span>
              <div className="flex flex-wrap gap-2">
                {STAFF_STATUSES.map((s) => (
                  <label
                    key={s}
                    className={cn(
                      'flex h-9 cursor-pointer items-center gap-2 rounded-full border px-3 text-[13px]',
                      field.value === s
                        ? 'border-primary-600 bg-primary-50 font-semibold text-primary-700'
                        : 'border-line text-ink-3 hover:bg-surface-subtle',
                    )}
                  >
                    <input
                      type="radio"
                      className="sr-only"
                      name={field.name}
                      value={s}
                      checked={field.value === s}
                      onChange={() => field.onChange(s)}
                    />
                    {s}
                  </label>
                ))}
              </div>
              <span className="text-xs text-ink-muted">{STATUS_HINT[field.value ?? 'Activo']}</span>
            </div>
          )}
        />

        {editing ? (
          accountText && <p className="rounded-lg bg-surface-subtle px-3 py-2.5 text-[13px] text-ink-3">{accountText}</p>
        ) : (
          <Controller
            control={control}
            name="invitar"
            render={({ field }) => (
              <fieldset className="flex flex-col gap-2 rounded-lg border border-line p-4">
                <legend className="px-1 text-[13px] font-semibold text-ink-2">Acceso al sistema</legend>
                {(
                  [
                    [
                      true,
                      'Invitar a crear su cuenta de usuario',
                      'Recibirá una invitación (caduca en 7 días). Aparecerá en Invitaciones.',
                    ],
                    [false, 'Sin acceso al sistema', 'Para personal que no usa SCI-PH (p. ej. conductores). Podrás invitarle más tarde.'],
                  ] as const
                ).map(([value, label, hint]) => (
                  <label
                    key={label}
                    className={cn(
                      'flex cursor-pointer gap-3 rounded-lg border px-3 py-2.5',
                      field.value === value ? 'border-primary-600 bg-primary-50' : 'border-line',
                    )}
                  >
                    <input
                      type="radio"
                      name="invitar"
                      checked={field.value === value}
                      onChange={() => field.onChange(value)}
                      className="mt-1 accent-primary-600"
                    />
                    <span className="flex flex-col">
                      <span className="text-sm font-medium text-ink">{label}</span>
                      <span className="text-xs text-ink-muted">{hint}</span>
                    </span>
                  </label>
                ))}
                {invitar && (
                  <Select
                    label="Rol"
                    placeholder="Selecciona un rol"
                    hint={puesto ? `Sugerido para ${puesto}: ${ROL_SUGERIDO[puesto]}.` : undefined}
                    options={roles.map((r) => ({ value: r.nombre, label: r.nombre }))}
                    error={errors.rol?.message}
                    containerClassName="mt-2"
                    {...register('rol')}
                  />
                )}
              </fieldset>
            )}
          />
        )}
      </form>
    </Modal>
  );
}
