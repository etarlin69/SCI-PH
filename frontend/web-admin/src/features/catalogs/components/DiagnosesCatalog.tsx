import { z } from 'zod';
import type { CatalogKind } from '../../../types';
import { CIE10_PATTERN } from '../../../services';
import type { CatalogConfig } from '../catalogConfig';
import { CatalogManager } from './CatalogManager';

const CATEGORIAS = ['Cardiovascular', 'Neurológico', 'Trauma', 'Respiratorio', 'Otros'] as const;

export const DIAGNOSES_CONFIG: CatalogConfig<'diagnosticos'> = {
  kind: 'diagnosticos',
  label: 'Diagnósticos',
  singular: 'diagnóstico',
  newLabel: 'Nuevo diagnóstico',
  createdLabel: 'Diagnóstico creado',
  description: 'Codificación CIE-10 usada en impresiones diagnósticas.',
  searchPlaceholder: 'Código CIE-10 o descripción',
  columns: [
    { field: 'codigo', header: 'Código CIE-10', mono: true, width: '120px' },
    { field: 'descripcion', header: 'Descripción', strong: true },
    { field: 'categoria', header: 'Categoría' },
  ],
  searchFields: ['codigo', 'descripcion'],
  filter: { field: 'categoria', label: 'Categoría', allLabel: 'Todas' },
  statuses: ['Activo', 'Inactivo'],
  usageHeader: 'Uso (episodios)',
  usageUnit: ' episodios',
  fields: [
    {
      name: 'codigo',
      label: 'Código CIE-10',
      required: true,
      placeholder: 'J18.9',
      hint: 'Formato CIE-10: letra + 2 dígitos (+ decimal). Ej.: J18.9',
      mono: true,
    },
    { name: 'descripcion', label: 'Descripción', required: true, placeholder: 'Ej.: Neumonía, no especificada' },
    { name: 'categoria', label: 'Categoría', required: true, type: 'select', options: CATEGORIAS },
  ],
  schema: z.object({
    codigo: z.string().trim().toUpperCase().regex(CIE10_PATTERN, 'Formato CIE-10: letra + 2 dígitos (+ decimal). Ej.: J18.9'),
    descripcion: z.string().trim().min(3, 'Escribe la descripción.'),
    categoria: z.string().min(1, 'Selecciona la categoría.'),
    estado: z.enum(['Activo', 'Inactivo']),
  }),
  emptyValues: { codigo: '', descripcion: '', categoria: '', estado: 'Activo' },
  itemName: (i) => `${i.codigo} · ${i.descripcion}`,
  csvImport: true,
  minWidth: '760px',
};

export interface DiagnosesCatalogProps {
  onCountChange?: (kind: CatalogKind, count: number) => void;
}

/** Catálogo CIE-10 (con importación CSV). */
export function DiagnosesCatalog({ onCountChange }: DiagnosesCatalogProps) {
  return <CatalogManager config={DIAGNOSES_CONFIG} onCountChange={onCountChange} />;
}
