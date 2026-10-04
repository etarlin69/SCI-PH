import { useEffect, useState, type FormEvent } from 'react';
import { Button, Card, FormError, Input, Switch } from '../../../components/shared';
import { useSubmit } from '../../../hooks/useSubmit';
import type { Profile, ProfileInput } from '../../../types';

export interface ProfileFormProps {
  profile: Profile;
  onSave: (input: ProfileInput) => Promise<Profile>;
  onSaved: (profile: Profile) => void;
}

const PHONE_PATTERN = /^[0-9+()\s-]{7,20}$/;

/** Datos personales y preferencias de notificación editables por el propio usuario. */
export function ProfileForm({ profile, onSave, onSaved }: ProfileFormProps) {
  const toValues = (p: Profile): ProfileInput => ({
    nombre: p.nombre,
    telefono: p.telefono,
    notificacionesEmail: p.notificacionesEmail,
    resumenSemanal: p.resumenSemanal,
  });
  const [values, setValues] = useState<ProfileInput>(() => toValues(profile));
  const [touched, setTouched] = useState(false);
  const submit = useSubmit();
  useEffect(() => setValues(toValues(profile)), [profile]);

  const errors = {
    nombre: values.nombre.trim().length < 3 ? 'Escribe tu nombre completo (mínimo 3 caracteres).' : undefined,
    telefono:
      values.telefono && !PHONE_PATTERN.test(values.telefono)
        ? 'Teléfono no válido (solo números, espacios, +, - y paréntesis).'
        : undefined,
  };
  const dirty = JSON.stringify(values) !== JSON.stringify(toValues(profile));

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (errors.nombre || errors.telefono) return;
    const saved = await submit.run(() => onSave(values));
    if (saved) {
      setTouched(false);
      onSaved(saved);
    }
  };

  return (
    <Card title="Datos personales" subtitle="Visible para los administradores y en los registros de auditoría.">
      <form noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        <FormError message={submit.error} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Nombre completo"
            required
            value={values.nombre}
            onChange={(e) => setValues((v) => ({ ...v, nombre: e.target.value }))}
            error={touched ? errors.nombre : undefined}
          />
          <Input label="Email" value={profile.email} disabled hint="Lo cambia un administrador desde Usuarios." />
          <Input
            label="Teléfono"
            type="tel"
            value={values.telefono}
            onChange={(e) => setValues((v) => ({ ...v, telefono: e.target.value }))}
            error={touched ? errors.telefono : undefined}
            placeholder="809-555-0100"
          />
          <Input label="Rol" value={profile.rol} disabled hint="Asignado por un administrador." />
        </div>
        <div className="flex flex-col gap-3 border-t border-line-soft pt-4">
          <Switch
            checked={values.notificacionesEmail}
            onChange={(c) => setValues((v) => ({ ...v, notificacionesEmail: c }))}
            label="Avisos por email"
            description="Alertas de seguridad e invitaciones que gestionas."
          />
          <Switch
            checked={values.resumenSemanal}
            onChange={(c) => setValues((v) => ({ ...v, resumenSemanal: c }))}
            label="Resumen semanal"
            description="Correo los lunes con los indicadores del Dashboard."
          />
        </div>
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button type="button" variant="secondary" disabled={!dirty || submit.submitting} onClick={() => setValues(toValues(profile))}>
            Descartar cambios
          </Button>
          <Button type="submit" disabled={!dirty} loading={submit.submitting} loadingText="Guardando…">
            Guardar cambios
          </Button>
        </div>
      </form>
    </Card>
  );
}
