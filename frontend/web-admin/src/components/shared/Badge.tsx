import type { ReactNode } from 'react';
import type {
  AmbulanceStatus,
  AuditResult,
  CatalogStatus,
  TransferStatus,
} from '../../types';
import { cn } from '../../utils/cn';

export type BadgeTone = 'success' | 'info' | 'warning' | 'danger' | 'neutral' | 'accent';

export interface BadgeProps {
  tone?: BadgeTone;
  dot?: boolean;
  children: ReactNode;
  className?: string;
}

const TONES: Record<BadgeTone, string> = {
  success: 'bg-success-50 text-success-600',
  info: 'bg-primary-100 text-primary-600',
  warning: 'bg-warning-50 text-warning-700',
  danger: 'bg-danger-50 text-danger-text',
  neutral: 'bg-[#EEF0F2] text-[#4A5563]',
  accent: 'bg-[#F1ECF8] text-[#553D84]',
};

export function Badge({ tone = 'neutral', dot = true, children, className }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-semibold',
        TONES[tone],
        className,
      )}
    >
      {dot && <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-current" aria-hidden="true" />}
      {children}
    </span>
  );
}

/** Tonos estándar por estado (mismo criterio que el canvas: suspendido/revocado en gris o rojo, pendiente en ámbar). */
export const CATALOG_STATUS_TONE: Record<CatalogStatus | AmbulanceStatus, BadgeTone> = {
  Activo: 'success',
  Inactivo: 'neutral',
  Disponible: 'success',
  'En uso': 'info',
  Mantenimiento: 'warning',
};

export const AUDIT_RESULT_TONE: Record<AuditResult, BadgeTone> = {
  Éxito: 'success',
  Error: 'danger',
};

/** Tono estándar para cada estado de transferencia. */
export const TRANSFER_STATUS_TONE: Record<TransferStatus, BadgeTone> = {
  Recibido: 'success',
  'En tránsito': 'info',
  Pendiente: 'warning',
};
