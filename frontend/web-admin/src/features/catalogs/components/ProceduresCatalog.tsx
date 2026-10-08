import { z } from 'zod';
import type { CatalogKind } from '../../../types';
import type { CatalogConfig } from '../catalogConfig';
import { CatalogManager } from './CatalogManager';

const ESPECIALIDADES = ['Emergencias', 'Trauma', 'Cardiología', 'Enfermería', 'Respiratorio'] as const;
const PROTOCOLOS = [
  'Soporte vital avanzado',
  'Vía aérea avanzada',
  'Trauma prehospitalario',
  'Control de hemorragias',
  'Código infarto',
  'Estándar',
];

export const PROCEDURES_CONFIG: CatalogConfig<'procedimientos'> = {
  kind: 'procedimientos',
  label: 'Procedimientos',
  singular: 'procedimiento',
  newLabel: 'Nuevo procedimiento',
  createdLabel: 'Procedimiento creado',
  description: 'Procedimientos clínicos y su protocolo de referencia.',
  searchPlaceholder: 'Código o descripción',
  columns: [
    { field: 'codigo', header: 'Código', mono: true, width: '110px' },
    { field: 'descripcion', header: 'Descripción', strong: true },
    { field: 'especialidad', header: 'Especialidad' },
    { field: 'protocolo', header: 'Protocolo de referencia' },
  ],
  searchFields: ['codigo', 'descripcion', 'protocolo'],
  filter: { field: 'especialidad', label: 'Especialidad', allLabel: 'Todas' },
  statuses: ['Activo', 'Inactivo'],
  usageHeader: 'Uso (episodios)',
  usageUnit: ' episodios',
  fields: [
    { name: 'codigo', label: 'Código', required: true, placeholder: 'PRC-011', hint: 'Debe ser único dentro del catálogo.', mono: true },
    { name: 'descripcion', label: 'Descripción', required: true, placeholder: 'Ej.: Cricotiroidotomía' },
    { name: 'especialidad', label: 'Especialidad', required: true, type: 'select', options: ESPECIALIDADES },
    { name: 'protocolo', label: 'Protocolo de referencia', required: true, placeholder: 'Ej.: Vía aérea avanzada', options: PROTOCOLOS },
  ],
  schema: z.object({
    codigo: z
      .string()
      .trim()
      .toUpperCase()
      .regex(/^PRC-\d{3,}$/, 'Formato PRC-000.'),
    descripcion: z.string().trim().min(3, 'Escribe la descripción.'),
    especialidad: z.string().min(1, 'Selecciona la especialidad.'),
    protocolo: z.string().trim().min(2, 'Indica el protocolo.'),
    estado: z.enum(['Activo', 'Inactivo']),
  }),
  emptyValues: { codigo: '', descripcion: '', especialidad: '', protocolo: '', estado: 'Activo' },
  itemName: (i) => `${i.codigo} · ${i.descripcion}`,
  minWidth: '860px',
};

export interface ProceduresCatalogProps {
  onCountChange?: (kind: CatalogKind, count: number) => void;
}

/** Catálogo de procedimientos clínicos. */
export function ProceduresCatalog({ onCountChange }: ProceduresCatalogProps) {
  return <CatalogManager config={PROCEDURES_CONFIG} onCountChange={onCountChange} />;
}
