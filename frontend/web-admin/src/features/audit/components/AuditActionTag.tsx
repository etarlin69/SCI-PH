import type { AuditAction } from '../../../types';
import { cn } from '../../../utils/cn';

export interface AuditActionTagProps {
  action: AuditAction;
}

const ACTION_CLASS: Record<AuditAction, string> = {
  'Inicio de sesión': 'bg-[#EEF0F2] text-ink-3',
  'Cierre de sesión': 'bg-[#EEF0F2] text-ink-3',
  Consultar: 'bg-[#F1ECF8] text-[#553D84]',
  Crear: 'bg-success-50 text-success-600',
  Actualizar: 'bg-primary-100 text-primary-600',
  Eliminar: 'bg-danger-50 text-danger-text',
  Revocar: 'bg-warning-50 text-warning-700',
  Exportar: 'bg-[#E3F2F2] text-[#16605F]',
};

/** Etiqueta compacta del tipo de acción de un evento de auditoría. */
export function AuditActionTag({ action }: AuditActionTagProps) {
  return <span className={cn('whitespace-nowrap rounded px-[7px] py-0.5 text-xs font-semibold', ACTION_CLASS[action])}>{action}</span>;
}
