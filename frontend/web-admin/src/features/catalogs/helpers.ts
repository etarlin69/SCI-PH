import type { CatalogItemMap, CatalogKind } from '../../types';
import type { CatalogConfig, CatalogFieldName, CatalogFormValues } from './catalogConfig';

/** Lee un campo de un elemento de catálogo como texto. */
export function fieldValue<K extends CatalogKind>(item: CatalogItemMap[K], field: CatalogFieldName<K> | 'estado'): string {
  const v = (item as unknown as Record<string, unknown>)[field];
  return v === undefined || v === null ? '' : String(v);
}

/** Valores iniciales del formulario a partir de un elemento o de `emptyValues`. */
export function toFormValues<K extends CatalogKind>(config: CatalogConfig<K>, item?: CatalogItemMap[K]): CatalogFormValues {
  const source = (item ?? config.emptyValues) as unknown as Record<string, unknown>;
  const values: CatalogFormValues = { estado: String(source.estado ?? config.statuses[0]) };
  config.fields.forEach((f) => {
    values[f.name] = source[f.name] === undefined ? '' : String(source[f.name]);
  });
  return values;
}

/** Normaliza para buscar sin distinguir mayúsculas ni tildes. */
export const normalize = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

export function filterCatalog<K extends CatalogKind>(
  items: CatalogItemMap[K][],
  config: CatalogConfig<K>,
  f: { q: string; filter: string; estado: string },
): CatalogItemMap[K][] {
  const q = normalize(f.q.trim());
  return items.filter(
    (i) =>
      (!q || config.searchFields.some((s) => normalize(fieldValue(i, s)).includes(q))) &&
      (!f.filter || !config.filter || fieldValue(i, config.filter.field) === f.filter) &&
      (!f.estado || fieldValue(i, 'estado') === f.estado),
  );
}
