import { z } from 'zod';
import type { ClinicInput } from '../../types';
import { CLINIC_STATUSES, CLINIC_TYPES } from './filters';

export const PHONE_PATTERN = /^\d{3}-\d{3}-\d{4}$/;
export const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

/** Esquema del formulario de clínica (mismas reglas y mensajes que el canvas). */
export function clinicSchema(takenNames: string[] = []) {
  const taken = new Set(takenNames.map((n) => n.trim().toLowerCase()));
  return z.object({
    nombre: z
      .string()
      .trim()
      .min(3, 'Escribe el nombre (mínimo 3 caracteres).')
      .max(80, 'Máximo 80 caracteres.')
      .refine((v) => !taken.has(v.toLowerCase()), 'Ya existe una clínica con ese nombre.'),
    tipo: z.string().refine((v) => (CLINIC_TYPES as string[]).includes(v), 'Selecciona el tipo.'),
    estado: z.enum(CLINIC_STATUSES as [ClinicInput['estado'], ...ClinicInput['estado'][]]),
    direccion: z.string().trim().min(5, 'Escribe la dirección.'),
    provincia: z.string().min(1, 'Selecciona la provincia.'),
    telefono: z.string().trim().min(1, 'El teléfono es obligatorio.').regex(PHONE_PATTERN, 'Formato esperado: 809-555-0100'),
    email: z.string().trim().min(1, 'El email es obligatorio.').regex(EMAIL_PATTERN, 'Formato inválido. Ejemplo: clinica@sci-ph.test'),
    contacto: z.string().trim().min(3, 'Indica la persona de contacto.'),
  });
}

export type ClinicFormValues = z.infer<ReturnType<typeof clinicSchema>>;

export const EMPTY_CLINIC_FORM: ClinicFormValues = {
  nombre: '',
  tipo: '',
  estado: 'Activa',
  direccion: '',
  provincia: '',
  telefono: '',
  email: '',
  contacto: '',
};

/** Valores del formulario → payload del servicio. */
export function toClinicInput(v: ClinicFormValues): ClinicInput {
  return {
    nombre: v.nombre.trim(),
    tipo: v.tipo as ClinicInput['tipo'],
    estado: v.estado,
    direccion: v.direccion.trim(),
    provincia: v.provincia,
    telefono: v.telefono.trim(),
    email: v.email.trim().toLowerCase(),
    contacto: v.contacto.trim(),
  };
}
