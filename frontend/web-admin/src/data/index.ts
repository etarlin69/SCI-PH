import raw from './mockData.json';
import type {
    Ambulancia,
    AuditEvent,
    CatalogData,
    CurrentUser,
    Diagnostico,
    Hospital,
    HospitalId,
    HospitalRecord,
    Medicamento,
    MetodoIdentificacion,
    PermissionChange,
    Procedimiento,
    TransferStatus,
    Turno,
    Usuario,
} from '../types';
import { addDays, diffDays, todayISO } from '../utils/dates';

export interface MockTransfer {
    id: string;
    diasAtras: number;
    hora: string;
    paciente: string;
    origen: string;
    destino: string;
    estado: TransferStatus;
}

export const USUARIOS = raw.usuarios as Usuario[];
export const HISTORIAL_PERMISOS = raw.historialPermisos as PermissionChange[];
export const KPIS_REFERENCIA = raw.kpis;
export const METODOS_REFERENCIA = raw.metodosIdentificacion as Record<MetodoIdentificacion, number>;
export const HOSPITALES = raw.hospitales as Hospital[];
export const HOSPITAL_IDS: HospitalId[] = HOSPITALES.map((h) => h.id);
export const TRANSFERENCIAS_MOCK = raw.transferenciasRecientes as MockTransfer[];

export const CATALOGOS_INICIALES: CatalogData = {
    medicamentos: raw.medicamentos as Medicamento[],
    diagnosticos: raw.diagnosticos as Diagnostico[],
    procedimientos: raw.procedimientos as Procedimiento[],
    hospitales: raw.hospitales as HospitalRecord[],
    ambulancias: raw.ambulancias as Ambulancia[],
    turnos: raw.turnos as Turno[],
};

function rebaseToToday(events: AuditEvent[]): AuditEvent[] {
    const latest = events.reduce((max, e) => (e.timestamp > max ? e.timestamp : max), '');
    const shift = diffDays(latest.slice(0, 10), todayISO());
    if (shift === 0) return events;
    return events.map((e) => ({ ...e, timestamp: `${addDays(e.timestamp.slice(0, 10), shift)}${e.timestamp.slice(10)}` }));
}

export const AUDIT_LOG_INICIAL: AuditEvent[] = rebaseToToday(raw.auditLog as AuditEvent[]);

export const CURRENT_USER: CurrentUser = {
    nombre: 'Admin SCIH',
    email: 'admin@scih.com',
    rol: 'Admin',
};
