import { z } from 'zod';
import { DEVICE_TYPES } from './helpers';

/** Paso 1 del alta de dispositivo. */
export const createDeviceSchema = z.object({
  nombre: z.string().trim().min(3, 'Escribe un nombre de al menos 3 caracteres.').max(60, 'Máximo 60 caracteres.'),
  tipo: z.string().refine((v) => (DEVICE_TYPES as string[]).includes(v), 'Selecciona un tipo.'),
  asignadoA: z.string().trim().max(80, 'Máximo 80 caracteres.'),
});

export type CreateDeviceFormValues = z.infer<typeof createDeviceSchema>;
