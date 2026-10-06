import { z } from 'zod';
import type { CatalogKind } from '../../../types';
import type { CatalogConfig } from '../catalogConfig';
import { CatalogManager } from './CatalogManager';

const PRESENTACIONES = ['Tableta', 'Tableta sublingual', 'Inyectable', 'Inhalador', 'Gas medicinal', 'Líquido'] as const;

export const MEDICATIONS_CONFIG: CatalogConfig<'medicamentos'> = {
  kind: 'medicamentos',
  label: 'Medicamentos',
  singular: 'medicamento',
  newLabel: 'Nuevo medicamento',
  createdLabel: 'Medicamento creado',
  description: 'Fármacos disponibles para registro en episodios prehospitalarios y hospitalarios.',
  searchPlaceholder: 'Código o nombre',
  columns: [
    { field: 'codigo', header: 'Código', mono: true, width: '110px' },
    { field: 'nombre', header: 'Nombre', strong: true },
    { field: 'concentracion', header: 'Concentración' },
    { field: 'presentacion', header: 'Presentación' },
    { field: 'unidad', header: 'Unidad' },
  ],
  searchFields: ['codigo', 'nombre'],
  filter: { field: 'presentacion', label: 'Presentación', allLabel: 'Todas' },
  statuses: ['Activo', 'Inactivo'],
  usageHeader: 'Uso (episodios)',
  usageUnit: ' episodios',
  fields: [
    { name: 'codigo', label: 'Código', required: true, placeholder: 'MED-011', hint: 'Debe ser único dentro del catálogo.', mono: true },
    { name: 'nombre', label: 'Nombre', required: true, placeholder: 'Ej.: Amiodarona' },
    { name: 'concentracion', label: 'Concentración', required: true, placeholder: 'Ej.: 150 mg/3 mL' },
    { name: 'presentacion', label: 'Presentación', required: true, type: 'select', options: PRESENTACIONES },
    { name: 'unidad', label: 'Unidad', required: true, placeholder: 'mg, mL, dosis…', options: ['mg', 'mL', 'mcg', 'dosis', 'L/min'] },
  ],
  schema: z.object({
    codigo: z
      .string()
      .trim()
      .toUpperCase()
      .regex(/^MED-\d{3,}$/, 'Formato MED-000.'),
    nombre: z.string().trim().min(2, 'Escribe el nombre.'),
    concentracion: z.string().trim().min(1, 'Indica la concentración.'),
    presentacion: z.string().min(1, 'Selecciona la presentación.'),
    unidad: z.string().trim().min(1, 'Indica la unidad.'),
    estado: z.enum(['Activo', 'Inactivo']),
  }),
  emptyValues: { codigo: '', nombre: '', concentracion: '', presentacion: '', unidad: '', estado: 'Activo' },
  itemName: (i) => `${i.codigo} · ${i.nombre}`,
  minWidth: '860px',
};

export interface MedicationsCatalogProps {
  onCountChange?: (kind: CatalogKind, count: number) => void;
}

/** Catálogo de medicamentos. */
export function MedicationsCatalog({ onCountChange }: MedicationsCatalogProps) {
  return <CatalogManager config={MEDICATIONS_CONFIG} onCountChange={onCountChange} />;
}
