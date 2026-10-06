import { z } from 'zod';
import type { CatalogKind } from '../../../types';
import type { CatalogConfig } from '../catalogConfig';
import { CatalogManager } from './CatalogManager';

export const AMBULANCES_CONFIG: CatalogConfig<'ambulancias'> = {
  kind: 'ambulancias',
  label: 'Ambulancias',
  singular: 'unidad',
  newLabel: 'Nueva unidad',
  createdLabel: 'Unidad creada',
  description: 'Flota prehospitalaria. Su tablet se gestiona en Dispositivos.',
  searchPlaceholder: 'Código, ubicación o personal',
  columns: [
    { field: 'codigo', header: 'Unidad', mono: true, width: '100px' },
    { field: 'tipo', header: 'Tipo' },
    { field: 'ubicacion', header: 'Ubicación', strong: true },
    { field: 'personal', header: 'Personal' },
  ],
  searchFields: ['codigo', 'ubicacion', 'personal'],
  filter: { field: 'tipo', label: 'Tipo', allLabel: 'Todos' },
  statuses: ['Disponible', 'En uso', 'Mantenimiento'],
  usageHeader: 'Episodios hoy',
  usageUnit: '',
  fields: [
    {
      name: 'codigo',
      label: 'Código de unidad',
      required: true,
      placeholder: 'AMB-23',
      hint: 'Debe ser único dentro del catálogo.',
      mono: true,
    },
    { name: 'tipo', label: 'Tipo', required: true, type: 'select', options: ['Avanzada', 'Intermedia', 'Básica'] },
    { name: 'ubicacion', label: 'Ubicación / base', required: true, placeholder: 'Ej.: Base Central' },
    {
      name: 'personal',
      label: 'Personal asignado',
      placeholder: 'Ej.: C. Pérez, R. Mejía',
      hint: 'Opcional. Separa los nombres con comas.',
    },
  ],
  schema: z.object({
    codigo: z
      .string()
      .trim()
      .toUpperCase()
      .regex(/^AMB-\d{2,}$/, 'Formato AMB-00.'),
    tipo: z.enum(['Avanzada', 'Intermedia', 'Básica'], { error: 'Selecciona el tipo.' }),
    ubicacion: z.string().trim().min(2, 'Indica la ubicación o base.'),
    personal: z
      .string()
      .trim()
      .transform((v) => v || 'Sin asignar'),
    estado: z.enum(['Disponible', 'En uso', 'Mantenimiento']),
  }),
  emptyValues: { codigo: '', tipo: 'Avanzada', ubicacion: '', personal: '', estado: 'Disponible' },
  itemName: (i) => `${i.codigo} · ${i.tipo}`,
  minWidth: '820px',
};

export interface AmbulancesCatalogProps {
  onCountChange?: (kind: CatalogKind, count: number) => void;
}

/** Catálogo de unidades de ambulancia (Disponible / En uso / Mantenimiento). */
export function AmbulancesCatalog({ onCountChange }: AmbulancesCatalogProps) {
  return <CatalogManager config={AMBULANCES_CONFIG} onCountChange={onCountChange} />;
}
