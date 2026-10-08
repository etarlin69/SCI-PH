import { cn } from '../../utils/cn';
import { initials } from '../../utils/formatters';

export interface AvatarProps {
  name: string;
  size?: 'sm' | 'md' | 'lg';
  className?: string;
}

const SIZES = { sm: 'h-7 w-7 text-[11px]', md: 'h-9 w-9 text-[13px]', lg: 'h-14 w-14 text-lg' };

/** Iniciales sobre fondo neutro (sin fotos: datos personales mínimos). */
export function Avatar({ name, size = 'md', className }: AvatarProps) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        'flex shrink-0 items-center justify-center rounded-full bg-primary-100 font-semibold text-primary-600',
        SIZES[size],
        className,
      )}
    >
      {initials(name)}
    </span>
  );
}
