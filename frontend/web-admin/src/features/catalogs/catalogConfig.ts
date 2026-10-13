import type { z } from 'zod';
import type { CatalogInput, CatalogItemMap, CatalogKind } from '../../types';

export type CatalogFieldName<K extends CatalogKind> = Exclude<keyof CatalogInput<K>, 'estado'> & string;

export type CatalogStatusOf<K extends CatalogKind> = CatalogItemMap[K]['estado'];

export type CatalogFormValues = Record<string, string>;

export interface CatalogColumn<K extends CatalogKind> {
  field: CatalogFieldName<K>;
  header: string;
  width?: string;
  mono?: boolean;
  strong?: boolean;
}

export interface CatalogField<K extends CatalogKind> {
  name: CatalogFieldName<K>;
  label: string;
  placeholder?: string;
  required?: boolean;
  hint?: string;
  mono?: boolean;
  type?: 'text' | 'select';
  options?: readonly string[];
}

export interface CatalogFilterConfig<K extends CatalogKind> {
  field: CatalogFieldName<K>;
  label: string;
  allLabel: string;
}

export interface CatalogConfig<K extends CatalogKind> {
  kind: K;
  label: string;
  singular: string;
  newLabel: string;
  createdLabel: string;
  description: string;
  searchPlaceholder: string;
  columns: CatalogColumn<K>[];
  searchFields: CatalogFieldName<K>[];
  filter?: CatalogFilterConfig<K>;
  statuses: readonly CatalogStatusOf<K>[];
  usageHeader: string;
  usageUnit: string;
  fields: CatalogField<K>[];
  schema: z.ZodType<CatalogInput<K>, unknown>;
  emptyValues: CatalogInput<K>;
  itemName: (item: CatalogItemMap[K]) => string;
  csvImport?: boolean;
  minWidth: string;
}
