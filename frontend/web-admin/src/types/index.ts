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

export type Rol = 'Médico' | 'Enfermero' | 'Admin' | 'Operador';
export type EstadoUsuario = 'Activo' | 'Suspendido';

export interface Usuario {
    id: string;
    nombre: string;
    email: string;
    roles: Rol[];
    estado: EstadoUsuario;

    createdAt: ISODate;

    updatedAt: string;
}

export interface CurrentUser {
    nombre: string;
    email: string;
    rol: Rol;
}

export type PermissionChangeType = 'Usuario creado' | 'Rol agregado' | 'Rol retirado' | 'Estado cambio';

export interface PermissionChange {
    id: string;
    userId: string;
    fecha: string;
    administrador: string;
    cambio: PermissionChangeType;
    detalle: string;
}

export type AuditEventType = 'crear' | 'actualizar' | 'eliminar' | 'login' | 'logout' | 'cambio_rol' | 'acceso';

export type AuditEntity =
    | 'Usuario'
    | 'Paciente'
    | 'Episodio'
    | 'Documento'
    | 'Medicamento'
    | 'Diagnóstico'
    | 'Procedimiento'
    | 'Hospital'
    | 'Ambulancia'
    | 'Turno'
    | 'Configuración';

export type AuditSnapshot = Record<string, unknown> | null;

export interface AuditEvent {
    id: string;

    timestamp: string;
    usuario: string;
    tipo: AuditEventType;
    entidad: AuditEntity;
    registroId: string;
    ip: string;
    navegador: string;
    sistema: string;
    before: AuditSnapshot;
    after: AuditSnapshot;
}

export interface AuditFiltersValue {
    range: DateRange;

    usuario: string;
    tipos: AuditEventType[];
    entidades: AuditEntity[];
}

export interface Medicamento {
    id: string;
    nombre: string;
    codigo: string;
    categoria: string;
    disponible: boolean;

    enUso?: boolean;
}

export interface Diagnostico {
    id: string;
    codigo: string;
    descripcion: string;
    sistemaAfectado: string;
    enUso?: boolean;
}

export type TipoProcedimiento = 'Quirúrgico' | 'Diagnóstico' | 'Terapéutico';

export interface Procedimiento {
    id: string;
    codigo: string;
    nombre: string;
    tipo: TipoProcedimiento;

    duracion: number;
    enUso?: boolean;
}

export interface HospitalRecord {
    id: string;
    nombre: string;
    region: string;
    direccion: string;
    telefono: string;
    lat: number;
    lng: number;
    color?: string;
}

export type EstadoAmbulancia = 'En Servicio' | 'Disponible' | 'En Mantenimiento';

export interface Ambulancia {
    id: string;
    matricula: string;
    estado: EstadoAmbulancia;
    ubicacion: string;
    hospitalId: string;
}

export interface Turno {
    id: string;
    nombre: string;

    inicio: string;

    fin: string;
    personal: number;
}

export interface CatalogData {
    medicamentos: Medicamento[];
    diagnosticos: Diagnostico[];
    procedimientos: Procedimiento[];
    hospitales: HospitalRecord[];
    ambulancias: Ambulancia[];
    turnos: Turno[];
}

export type CatalogKey = keyof CatalogData;

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

export interface RecentTransfer {
    id: string;
    paciente: string;
    origen: string;
    destino: string;
    fecha: ISODate;
    hora: string;
    estado: TransferStatus;
}

export interface DashboardData {
    kpis: DashboardKpis;
    trend: TrendPoint[];
    trendChange: number;
    methods: MethodSlice[];
    hospitals: HospitalCount[];
    events: RecentTransfer[];
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
