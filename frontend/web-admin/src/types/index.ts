export type ISODate = string;

export interface DateRange {
    from: ISODate;
    to: ISODate;
}

export type HospitalId = 'central' | 'norte' | 'sanrafael' | 'este' | 'infantil';

export interface Hospital {
    id: HospitalId;
    nombre: string;
    color: string;
}

// Roles reales del backend (SecurityRoles). Solo se alinean los tipos: la autorización llegará con el AuthProvider.
export type Role = 'Administrator' | 'Doctor' | 'Nurse' | 'Paramedic' | 'Receptionist' | 'Patient';

export interface CurrentUser {
    nombre: string;
    email: string;
    rol: Role;
}

export type MetodoIdentificacion = 'ocr' | 'qr' | 'huella' | 'manual' | 'nn';

export type MetricType = 'episodios' | 'transferencias' | 'identificaciones';

export interface DashboardKpis {
    episodiosActivos: number;
    episodiosActivosDiaAnterior: number;
    transferencias: number;
    transferenciasFallidas: number;
    tasaExito: number | null;
    pacientes: number;
    pacientesNN: number;
    pctIdentificados: number | null;
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

// Resumen agregado por destino: el Dashboard administrativo no muestra datos de pacientes.
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
