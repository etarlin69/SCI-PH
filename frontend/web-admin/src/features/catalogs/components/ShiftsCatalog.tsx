import { z } from 'zod';
import type { CatalogKind } from '../../../types';
import type { CatalogConfig } from '../catalogConfig';
import { CatalogManager } from './CatalogManager';

const HORARIO = /^([01]\d|2[0-3]):[0-5]\d\s*[–-]\s*([01]\d|2[0-3]):[0-5]\d$/;

export const SHIFTS_CONFIG: CatalogConfig<'turnos'> = {
  kind: 'turnos',
  label: 'Turnos',
  singular: 'turno',
  newLabel: 'Nuevo turno',
  createdLabel: 'Turno creado',
  description: 'Turnos de guardia usados en las asignaciones de Personal.',
  searchPlaceholder: 'Nombre o área',
  columns: [
    { field: 'nombre', header: 'Nombre', strong: true },
    { field: 'horario', header: 'Horario', mono: true },
    { field: 'area', header: 'Área' },
  ],
  searchFields: ['nombre', 'area'],
  statuses: ['Activo', 'Inactivo'],
  usageHeader: 'Personas asignadas',
  usageUnit: ' personas',
  fields: [
    { name: 'nombre', label: 'Nombre', required: true, placeholder: 'Ej.: Refuerzo nocturno', hint: 'Debe ser único dentro del catálogo.' },
    { name: 'horario', label: 'Horario', required: true, placeholder: '07:00 – 15:00', hint: 'Formato 07:00 – 15:00', mono: true },
    { name: 'area', label: 'Área', required: true, placeholder: 'Ej.: Emergencias · Clínica Central' },
  ],
  schema: z.object({
    nombre: z.string().trim().min(3, 'Escribe un nombre de al menos 3 caracteres.'),
    horario: z
      .string()
      .trim()
      .regex(HORARIO, 'Formato 07:00 – 15:00')
      .transform((v) => v.replace(/\s*[–-]\s*/, ' – ')),
    area: z.string().trim().min(2, 'Indica el área.'),
    estado: z.enum(['Activo', 'Inactivo']),
  }),
  emptyValues: { nombre: '', horario: '', area: '', estado: 'Activo' },
  itemName: (i) => i.nombre,
  minWidth: '700px',
};

export interface ShiftsCatalogProps {
  onCountChange?: (kind: CatalogKind, count: number) => void;
}

/** Catálogo de turnos de guardia. */
export function ShiftsCatalog({ onCountChange }: ShiftsCatalogProps) {
  return <CatalogManager config={SHIFTS_CONFIG} onCountChange={onCountChange} />;
}
