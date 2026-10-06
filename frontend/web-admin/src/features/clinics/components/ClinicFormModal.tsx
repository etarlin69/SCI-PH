import { useEffect, useMemo, useState } from 'react';
import { Controller, useForm, type FieldPath } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { AlertTriangle } from 'lucide-react';
import type { Clinic } from '../../../types';
import { clinicsService, errorMessage, isServiceError } from '../../../services';
import { Button, Input, Modal, Select, useToast } from '../../../components/shared';
import { cn } from '../../../utils/cn';
import { CLINIC_TYPES } from '../filters';
import { EMPTY_CLINIC_FORM, clinicSchema, toClinicInput, type ClinicFormValues } from '../schema';
import { ClinicStatusField } from './ClinicStatusField';

export interface ClinicFormModalProps {
  open: boolean;
  /** Clínica a editar; `null` para dar de alta una nueva. */
  clinic: Clinic | null;
  takenNames?: string[];
  onClose: () => void;
  onSaved: (clinic: Clinic, mode: 'create' | 'edit') => void;
}

const FIELDS: FieldPath<ClinicFormValues>[] = ['nombre', 'tipo', 'estado', 'direccion', 'provincia', 'telefono', 'email', 'contacto'];

/** Formulario de alta/edición de clínica (modal). */
export function ClinicFormModal({ open, clinic, takenNames = [], onClose, onSaved }: ClinicFormModalProps) {
  const toast = useToast();
  const isEdit = clinic !== null;
  const [serverError, setServerError] = useState<string | null>(null);

  const schema = useMemo(
    () => clinicSchema(takenNames.filter((n) => n.toLowerCase() !== clinic?.nombre.toLowerCase())),
    [takenNames, clinic],
  );

  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isDirty, isSubmitting, submitCount },
  } = useForm<ClinicFormValues>({ resolver: zodResolver(schema), defaultValues: EMPTY_CLINIC_FORM, mode: 'onTouched' });

  useEffect(() => {
    if (!open) return;
    setServerError(null);
    reset(
      clinic
        ? {
            nombre: clinic.nombre,
            tipo: clinic.tipo,
            estado: clinic.estado,
            direccion: clinic.direccion,
            provincia: clinic.provincia,
            telefono: clinic.telefono,
            email: clinic.email,
            contacto: clinic.contacto,
          }
        : EMPTY_CLINIC_FORM,
    );
  }, [open, clinic, reset]);

  const submit = handleSubmit(async (values) => {
    setServerError(null);
    try {
      const input = toClinicInput(values);
      const saved = clinic ? await clinicsService.update(clinic.id, input) : await clinicsService.create(input);
      toast(
        isEdit
          ? { title: 'Clínica actualizada', description: `${saved.nombre} · cambios guardados.`, tone: 'success' }
          : { title: 'Clínica creada', description: `${saved.nombre} ya puede recibir personal y dispositivos.`, tone: 'success' },
      );
      onSaved(saved, isEdit ? 'edit' : 'create');
    } catch (e) {
      const field = isServiceError(e) ? e.details.field : undefined;
      if (typeof field === 'string' && (FIELDS as string[]).includes(field)) {
        setError(field as FieldPath<ClinicFormValues>, { type: 'server', message: errorMessage(e) }, { shouldFocus: true });
      } else {
        setServerError(errorMessage(e));
      }
    }
  });

  const errorCount = Object.keys(errors).length;
  const saving = isSubmitting;
  const summary = saving
    ? 'Guardando…'
    : errorCount && submitCount > 0
      ? errorCount === 1
        ? '1 campo requiere atención'
        : `${errorCount} campos requieren atención`
      : isEdit && !isDirty
        ? 'Sin cambios todavía'
        : '';

  return (
    <Modal
      open={open}
      onClose={onClose}
      dismissible={!saving}
      size="md"
      title={isEdit ? 'Editar clínica' : 'Nueva clínica'}
      description={isEdit ? `${clinic.id} · los campos con * son obligatorios` : 'Los campos con * son obligatorios.'}
      footer={
        <div className="flex w-full flex-col-reverse gap-3 sm:flex-row sm:items-center sm:justify-between">
          <span
            role="status"
            aria-live="polite"
            className={cn('text-[13px]', errorCount && submitCount ? 'text-danger-text' : 'text-ink-muted')}
          >
            {summary}
          </span>
          <div className="flex flex-wrap justify-end gap-2.5">
            <Button variant="secondary" onClick={onClose} disabled={saving}>
              Cancelar
            </Button>
            <Button type="submit" form="clinic-form" loading={saving} loadingText="Guardando…" disabled={isEdit && !isDirty}>
              {serverError ? 'Reintentar' : isEdit ? 'Guardar cambios' : 'Crear clínica'}
            </Button>
          </div>
        </div>
      }
    >
      <form id="clinic-form" noValidate onSubmit={(e) => void submit(e)} className="grid grid-cols-1 gap-x-4 gap-y-4 sm:grid-cols-2">
        {serverError && (
          <div
            role="alert"
            className="flex items-start gap-2.5 rounded-lg bg-danger-50 px-3.5 py-3 text-[13px] leading-snug text-danger-800 sm:col-span-2"
          >
            <AlertTriangle size={16} className="mt-0.5 shrink-0" aria-hidden="true" />
            <span>
              <strong className="font-semibold">No se pudo guardar.</strong> {serverError} Tus datos siguen en el formulario.
            </span>
          </div>
        )}
        <Input
          label="Nombre"
          required
          autoComplete="off"
          placeholder="Ej.: Clínica Santa Ana"
          hint="Como aparecerá en traslados y reportes."
          error={errors.nombre?.message}
          containerClassName="sm:col-span-2"
          {...register('nombre')}
        />
        <Select
          label="Tipo"
          required
          placeholder="Selecciona un tipo"
          options={CLINIC_TYPES.map((t) => ({ value: t, label: t }))}
          error={errors.tipo?.message}
          {...register('tipo')}
        />
        <Controller
          control={control}
          name="estado"
          render={({ field }) => <ClinicStatusField value={field.value} onChange={field.onChange} disabled={saving} />}
        />
        <Input
          label="Dirección"
          required
          autoComplete="street-address"
          placeholder="Calle y número"
          error={errors.direccion?.message}
          containerClassName="sm:col-span-2"
          {...register('direccion')}
        />
        <Select
          label="Provincia"
          required
          placeholder="Selecciona la provincia"
          options={clinicsService.provincias.map((p) => ({ value: p, label: p }))}
          error={errors.provincia?.message}
          {...register('provincia')}
        />
        <Input
          label="Teléfono"
          required
          type="tel"
          inputMode="tel"
          placeholder="809-555-0100"
          hint="Línea de emergencias de la clínica."
          className="font-mono"
          error={errors.telefono?.message}
          {...register('telefono')}
        />
        <Input
          label="Email"
          required
          type="email"
          autoComplete="off"
          placeholder="clinica@sci-ph.test"
          error={errors.email?.message}
          {...register('email')}
        />
        <Input
          label="Persona de contacto"
          required
          autoComplete="off"
          placeholder="Nombre y apellidos"
          hint="Responsable de coordinación con SCI-PH."
          error={errors.contacto?.message}
          {...register('contacto')}
        />
      </form>
    </Modal>
  );
}
