export type CatalogKind = 'medicamentos' | 'diagnosticos' | 'procedimientos' | 'ambulancias' | 'turnos';

export type CatalogStatus = 'Activo' | 'Inactivo';
export type AmbulanceStatus = 'Disponible' | 'En uso' | 'Mantenimiento';

interface CatalogItemBase {
  id: string;
  uso: number;
}

export interface Medication extends CatalogItemBase {
  codigo: string;
  nombre: string;
  concentracion: string;
  presentacion: string;
  unidad: string;
  estado: CatalogStatus;
}

export interface Diagnosis extends CatalogItemBase {
  codigo: string;
  descripcion: string;
  categoria: string;
  estado: CatalogStatus;
}

export interface Procedure extends CatalogItemBase {
  codigo: string;
  descripcion: string;
  especialidad: string;
  protocolo: string;
  estado: CatalogStatus;
}

export type AmbulanceType = 'Avanzada' | 'Intermedia' | 'Básica';

export interface Ambulance extends CatalogItemBase {
  codigo: string;
  tipo: AmbulanceType;
  ubicacion: string;
  personal: string;
  estado: AmbulanceStatus;
}

export interface Shift extends CatalogItemBase {
  nombre: string;
  horario: string;
  area: string;
  estado: CatalogStatus;
}

export interface CatalogItemMap {
  medicamentos: Medication;
  diagnosticos: Diagnosis;
  procedimientos: Procedure;
  ambulancias: Ambulance;
  turnos: Shift;
}

export type CatalogItem<K extends CatalogKind = CatalogKind> = CatalogItemMap[K];

export type CatalogInput<K extends CatalogKind> = Omit<CatalogItemMap[K], 'id' | 'uso'>;

export interface CsvImportIssue {
  fila: number;
  mensaje: string;
}

export interface CsvImportPreview {
  archivo: string;
  validas: CatalogInput<'diagnosticos'>[];
  errores: CsvImportIssue[];
}
