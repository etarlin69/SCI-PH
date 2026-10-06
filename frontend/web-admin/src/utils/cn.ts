import { clsx, type ClassValue } from 'clsx';

/** Une clases condicionales de Tailwind. */
export function cn(...inputs: ClassValue[]): string {
  return clsx(inputs);
}
