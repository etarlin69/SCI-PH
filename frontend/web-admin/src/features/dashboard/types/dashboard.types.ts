import type { ISODate } from '../../../types/common';

export type HospitalId = 'central' | 'norte' | 'sanrafael' | 'este' | 'infantil';

export interface Hospital {
  id: HospitalId;
  nombre: string;
  color: string;
}

export type MetodoIdentificacion = 'ocr' | 'qr' | 'huella' | 'manual' | 'nn';

export type MetricType = 'episodios' | 'transferencias' | 'identificaciones';

export interface DashboardKpis {
  episodiosActivos: number;
  episodiosActivosDiaAnterior: number;
  transferencias: number;
  transferenciasFallidas: number;
  /** % de transferencias exitosas; `null` si no hubo transferencias. */
  tasaExito: number | null;
  pacientes: number;
  pacientesNN: number;
  /** % de pacientes identificados; `null` si no hubo pacientes. */
  pctIdentificados: number | null;
  /** Minutos; `null` si no hubo episodios. */
  tiempoPromedioRespuesta: number | null;
}

export interface TrendPoint {
  date: ISODate;
  completados: number;
  activos: number;
}

export interface MethodSlice {
  key: MetodoIdentificacion;
  label: string;
  value: number;
  color: string;
}

export interface HospitalCount {
  hospital: Hospital;
  episodios: number;
}

export type TransferStatus = 'Recibido' | 'En tránsito' | 'Pendiente';

export interface TransferSummary {
  destino: string;
  recibidas: number;
  enTransito: number;
  pendientes: number;
  total: number;
}

export interface DashboardData {
  kpis: DashboardKpis;
  trend: TrendPoint[];
  trendChange: number;
  methods: MethodSlice[];
  hospitals: HospitalCount[];
  transfers: TransferSummary[];
}

export type WidgetId = 'kpis' | 'trend' | 'methods' | 'hospitals' | 'events';

export interface WidgetConfig {
  id: WidgetId;
  label: string;
  visible: boolean;
}

export interface SavedView {
  name: string;
  widgets: WidgetConfig[];
}
