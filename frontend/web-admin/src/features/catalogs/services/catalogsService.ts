import { AMBULANCES_MOCK, DIAGNOSES_MOCK, MEDICATIONS_MOCK, PROCEDURES_MOCK, SHIFTS_MOCK } from '../../../data/mock';
import type { CatalogInput, CatalogItemMap, CatalogKind, CsvImportIssue, CsvImportPreview } from '../../../types';
import { parseCsv } from '../../../utils/csv';
import { ServiceError } from '../../../services/core/errors';
import { mockList, mockWrite, notFound } from '../../../services/core/mock';
import { record } from '../../audit/services/auditService';

type Store = { [K in CatalogKind]: CatalogItemMap[K][] };

const store: Store = {
  medicamentos: [...MEDICATIONS_MOCK],
  diagnosticos: [...DIAGNOSES_MOCK],
  procedimientos: [...PROCEDURES_MOCK],
  ambulancias: [...AMBULANCES_MOCK],
  turnos: [...SHIFTS_MOCK],
};

/** Campo que identifica de forma única cada elemento (y que se muestra en mensajes). */
export const CATALOG_KEY_FIELD: { [K in CatalogKind]: keyof CatalogInput<K> } = {
  medicamentos: 'codigo',
  diagnosticos: 'codigo',
  procedimientos: 'codigo',
  ambulancias: 'codigo',
  turnos: 'nombre',
};

const LABEL: Record<CatalogKind, string> = {
  medicamentos: 'Medicamentos',
  diagnosticos: 'Diagnósticos',
  procedimientos: 'Procedimientos',
  ambulancias: 'Ambulancias',
  turnos: 'Turnos',
};

/** Estado que se aplica al "desactivar" un elemento en uso. */
export const CATALOG_OFF_STATUS: { [K in CatalogKind]: CatalogItemMap[K]['estado'] } = {
  medicamentos: 'Inactivo',
  diagnosticos: 'Inactivo',
  procedimientos: 'Inactivo',
  ambulancias: 'Mantenimiento',
  turnos: 'Inactivo',
};

export const CIE10_PATTERN = /^[A-Z]\d{2}(\.\d{1,2})?$/;

function keyOf<K extends CatalogKind>(kind: K, item: CatalogInput<K> | CatalogItemMap[K]): string {
  return String(item[CATALOG_KEY_FIELD[kind] as keyof typeof item]);
}

function assertUnique<K extends CatalogKind>(kind: K, input: CatalogInput<K>, exceptId?: string): void {
  const key = keyOf(kind, input).trim().toLowerCase();
  const items = store[kind] as CatalogItemMap[K][];
  if (items.some((i) => i.id !== exceptId && keyOf(kind, i).toLowerCase() === key)) {
    throw new ServiceError('CONFLICT', 'Ya existe un registro con este valor; debe ser único.', { field: CATALOG_KEY_FIELD[kind] });
  }
}

function find<K extends CatalogKind>(kind: K, id: string): CatalogItemMap[K] {
  return (store[kind] as CatalogItemMap[K][]).find((i) => i.id === id) ?? notFound('El registro');
}

export const catalogsService = {
  list<K extends CatalogKind>(kind: K): Promise<CatalogItemMap[K][]> {
    return mockList(() => store[kind] as CatalogItemMap[K][]);
  },

  counts(): Promise<Record<CatalogKind, number>> {
    return mockList(() => Object.entries(store).map(([k, v]) => [k, v.length] as const)).then(
      (pairs) => Object.fromEntries(pairs) as Record<CatalogKind, number>,
    );
  },

  create<K extends CatalogKind>(kind: K, input: CatalogInput<K>): Promise<CatalogItemMap[K]> {
    return mockWrite(`catalogs.create.${kind}`, () => {
      assertUnique(kind, input);
      const item = { ...input, id: `${kind.slice(0, 3)}-${Date.now()}`, uso: 0 } as CatalogItemMap[K];
      (store[kind] as CatalogItemMap[K][]).unshift(item);
      record({ accion: 'Crear', recurso: `Catálogo ${LABEL[kind]} · ${keyOf(kind, item)}`, descripcion: 'Elemento de catálogo creado.' });
      return item;
    });
  },

  update<K extends CatalogKind>(kind: K, id: string, input: CatalogInput<K>): Promise<CatalogItemMap[K]> {
    return mockWrite(`catalogs.update.${kind}`, () => {
      assertUnique(kind, input, id);
      const item = find(kind, id);
      Object.assign(item, input);
      record({
        accion: 'Actualizar',
        recurso: `Catálogo ${LABEL[kind]} · ${keyOf(kind, item)}`,
        descripcion: 'Elemento de catálogo actualizado.',
      });
      return item;
    });
  },

  /** Error IN_USE si `uso > 0` (se debe desactivar en su lugar, para conservar la trazabilidad). */
  remove(kind: CatalogKind, id: string): Promise<void> {
    return mockWrite(`catalogs.remove.${kind}`, () => {
      const item = find(kind, id);
      if (item.uso > 0) throw new ServiceError('IN_USE', `Está referenciado en ${item.uso} registros.`, { uso: item.uso });
      const items = store[kind] as { id: string }[];
      items.splice(
        items.findIndex((i) => i.id === id),
        1,
      );
      record({
        accion: 'Eliminar',
        recurso: `Catálogo ${LABEL[kind]} · ${keyOf(kind, item)}`,
        descripcion: 'Elemento de catálogo eliminado.',
      });
    });
  },

  deactivate<K extends CatalogKind>(kind: K, id: string): Promise<CatalogItemMap[K]> {
    return mockWrite(`catalogs.deactivate.${kind}`, () => {
      const item = find(kind, id) as CatalogItemMap[K] & { estado: CatalogItemMap[K]['estado'] };
      const antes = item.estado;
      item.estado = CATALOG_OFF_STATUS[kind];
      record({
        accion: 'Actualizar',
        recurso: `Catálogo ${LABEL[kind]} · ${keyOf(kind, item)}`,
        descripcion: 'Elemento desactivado.',
        cambios: [{ campo: 'Estado', antes, despues: item.estado }],
      });
      return item;
    });
  },

  /** Valida un CSV de diagnósticos (columnas codigo, descripcion, categoria) sin importarlo. */
  async previewDiagnosesImport(file: File): Promise<CsvImportPreview> {
    const text = await file.text();
    return mockWrite('catalogs.import.preview', () => {
      const rows = parseCsv(text);
      const [header, ...data] = rows;
      const cols = (header ?? []).map((h) => h.trim().toLowerCase());
      const idx = { codigo: cols.indexOf('codigo'), descripcion: cols.indexOf('descripcion'), categoria: cols.indexOf('categoria') };
      if (idx.codigo < 0 || idx.descripcion < 0) {
        throw new ServiceError('VALIDATION', 'El archivo debe tener las columnas codigo, descripcion y categoria.');
      }
      const errores: CsvImportIssue[] = [];
      const validas: CatalogInput<'diagnosticos'>[] = [];
      const seen = new Set(store.diagnosticos.map((d) => d.codigo.toUpperCase()));
      data.forEach((r, i) => {
        const fila = i + 2;
        const codigo = (r[idx.codigo] ?? '').trim().toUpperCase();
        const descripcion = (r[idx.descripcion] ?? '').trim();
        if (!CIE10_PATTERN.test(codigo)) return errores.push({ fila, mensaje: `«${codigo || 'vacío'}» no es un código CIE-10 válido` });
        if (seen.has(codigo)) return errores.push({ fila, mensaje: `el código ${codigo} ya existe` });
        if (!descripcion) return errores.push({ fila, mensaje: 'falta la descripción' });
        seen.add(codigo);
        validas.push({ codigo, descripcion, categoria: (r[idx.categoria] ?? 'Otros').trim() || 'Otros', estado: 'Activo' });
      });
      return { archivo: file.name, validas, errores };
    });
  },

  commitDiagnosesImport(preview: CsvImportPreview): Promise<number> {
    return mockWrite('catalogs.import.commit', () => {
      preview.validas.forEach((v, i) => store.diagnosticos.unshift({ ...v, id: `dx-imp-${Date.now()}-${i}`, uso: 0 }));
      record({
        accion: 'Crear',
        recurso: 'Catálogo Diagnósticos',
        descripcion: `Importación CSV: ${preview.validas.length} diagnósticos añadidos, ${preview.errores.length} filas omitidas.`,
      });
      return preview.validas.length;
    });
  },
};
