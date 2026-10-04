import type { ISODate } from '../../../types/common';

export type AuditAction =
  'Inicio de sesión' | 'Cierre de sesión' | 'Consultar' | 'Crear' | 'Actualizar' | 'Eliminar' | 'Revocar' | 'Exportar';

export type AuditResult = 'Éxito' | 'Error';

export interface AuditChange {
  campo: string;
  antes: string;
  despues: string;
}

export interface AuditEvent {
  id: string;
  timestamp: string;
  usuario: string;
  email: string;
  rol: string;
  accion: AuditAction;
  recurso: string;
  resultado: AuditResult;
  error?: string;
  descripcion: string;
  ip: string;
  userAgent: string;
  cambios: AuditChange[];
  hash: string;
}

export interface AuditFilters {
  q: string;
  from: ISODate;
  to: ISODate;
  usuario: string;
  accion: AuditAction | '';
  resultado: AuditResult | '';
}

export type AuditExportFormat = 'csv' | 'pdf';

export type AuditRecordInput = Pick<AuditEvent, 'accion' | 'recurso' | 'descripcion'> &
  Partial<Pick<AuditEvent, 'resultado' | 'error' | 'cambios'>>;
