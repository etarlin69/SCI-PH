import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { DEPARTAMENTOS, TURNOS } from '../../../data/mock';
import type { Assignment, AssignmentInput, Departamento } from '../../../types';
import { Button, FormError, Input, Modal, Select } from '../../../components/shared';
import { useSubmit } from '../../../hooks/useSubmit';
import { todayISO } from '../../../utils/dates';

const schema = z.object({
  lugar: z.string().min(1, 'Selecciona la clínica o unidad.'),
  departamento: z.string().min(1, 'Selecciona el departamento.'),
  turno: z.string().min(1, 'Selecciona el turno.'),
  desde: z.string().min(1, 'Indica la fecha de inicio.'),
});
type FormValues = z.infer<typeof schema>;

export interface AssignmentModalProps {
  open: boolean;
  staffName: string;
  places: string[];
  onClose: () => void;
  onSubmit: (input: AssignmentInput) => Promise<Assignment>;
  onSaved: (a: Assignment) => void;
}

/** Nueva asignación a una clínica o unidad, con departamento, turno y fecha de inicio. */
export function AssignmentModal({ open, staffName, places, onClose, onSubmit, onSaved }: AssignmentModalProps) {
  const submit = useSubmit();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  useEffect(() => {
    if (open) {
      reset({ lugar: '', departamento: '', turno: '', desde: todayISO() });
      submit.reset();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const save = handleSubmit(async (v) => {
    const a = await submit.run(() => onSubmit({ ...v, departamento: v.departamento as Departamento }));
    if (a) onSaved(a);
  });

  return (
    <Modal
      open={open}
      onClose={onClose}
      dismissible={!submit.submitting}
      title="Nueva asignación"
      description={`${staffName} · Los campos con * son obligatorios.`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submit.submitting}>
            Cancelar
          </Button>
          <Button type="submit" form="assignment-form" loading={submit.submitting} loadingText="Guardando…">
            Guardar asignación
          </Button>
        </>
      }
    >
      <form id="assignment-form" noValidate onSubmit={save} className="flex flex-col gap-4">
        <FormError message={submit.error && `No se pudo guardar. ${submit.error}`} />
        <Select
          label="Clínica o unidad"
          required
          placeholder="Selecciona"
          options={places.map((p) => ({ value: p, label: p }))}
          error={errors.lugar?.message}
          {...register('lugar')}
        />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Select
            label="Departamento"
            required
            placeholder="Selecciona"
            options={DEPARTAMENTOS.map((d) => ({ value: d, label: d }))}
            error={errors.departamento?.message}
            {...register('departamento')}
          />
          <Select
            label="Turno"
            required
            placeholder="Selecciona"
            options={TURNOS.map((t) => ({ value: t, label: t }))}
            error={errors.turno?.message}
            {...register('turno')}
          />
        </div>
        <Input label="Desde" type="date" required error={errors.desde?.message} {...register('desde')} />
        <p className="text-xs text-ink-muted">La asignación actual del mismo tipo seguirá activa.</p>
      </form>
    </Modal>
  );
}
