import { Monitor, Smartphone, Tablet, type LucideIcon } from 'lucide-react';
import type { DeviceType } from '../../../types';
import { cn } from '../../../utils/cn';

export interface DeviceTypeIconProps {
  tipo: DeviceType;
  size?: number;
  boxed?: boolean;
  className?: string;
}

const ICONS: Record<DeviceType, LucideIcon> = { Tablet, Móvil: Smartphone, Escritorio: Monitor };

/** Icono decorativo del tipo de dispositivo (el tipo se muestra siempre también como texto). */
export function DeviceTypeIcon({ tipo, size = 16, boxed = false, className }: DeviceTypeIconProps) {
  const Icon = ICONS[tipo];
  if (!boxed) return <Icon size={size} aria-hidden="true" className={cn('shrink-0', className)} />;
  return (
    <span
      aria-hidden="true"
      className={cn('flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-primary-100 text-primary-600', className)}
    >
      <Icon size={size} />
    </span>
  );
}
