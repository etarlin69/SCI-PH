import type { AuditEntity, AuditEventType, EstadoAmbulancia, Rol, TipoProcedimiento } from '../types';

export const ROLES: { value: Rol; description: string }[] = [
    { value: 'Médico', description: 'Episodios y documentos clínicos' },
    { value: 'Enfermero', description: 'Registro de signos y cuidados' },
    { value: 'Admin', description: 'Usuarios, catálogos y auditoría' },
    { value: 'Operador', description: 'Despacho y transferencias' },
];

export const AUDIT_EVENT_LABEL: Record<AuditEventType, string> = {
    crear: 'Crear',
    actualizar: 'Actualizar',
    eliminar: 'Eliminar',
    login: 'Login',
    logout: 'Logout',
    cambio_rol: 'Cambio de rol',
    acceso: 'Acceso',
};

export const AUDIT_EVENT_TYPES = Object.keys(AUDIT_EVENT_LABEL) as AuditEventType[];

export const AUDIT_ENTITIES: AuditEntity[] = [
    'Usuario',
    'Paciente',
    'Episodio',
    'Documento',
    'Medicamento',
    'Diagnóstico',
    'Procedimiento',
    'Hospital',
    'Ambulancia',
    'Turno',
    'Configuración',
];

export const CATEGORIAS_MEDICAMENTO = [
    'Analgésico',
    'Antibiótico',
    'Antiagregante',
    'Antiarrítmico',
    'Anticonvulsivante',
    'Antídoto',
    'Broncodilatador',
    'Solución intravenosa',
    'Vasodilatador',
    'Vasopresor',
    'Otro',
];

export const SISTEMAS_AFECTADOS = [
    'Cardiovascular',
    'Respiratorio',
    'Neurológico',
    'Gastrointestinal',
    'Traumatológico',
    'Endocrino',
    'Otros',
];

export const TIPOS_PROCEDIMIENTO: TipoProcedimiento[] = ['Quirúrgico', 'Diagnóstico', 'Terapéutico'];

export const ESTADOS_AMBULANCIA: EstadoAmbulancia[] = ['En Servicio', 'Disponible', 'En Mantenimiento'];

export const PAGE_SIZE = { usuarios: 10, auditoria: 25, catalogos: 10 } as const;
