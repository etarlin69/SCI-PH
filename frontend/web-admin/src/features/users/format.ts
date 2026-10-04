import type { DateTime, ISODate } from '../../types';
import { diffDays, formatDMY, todayISO } from '../../utils/dates';

/** `YYYY-MM-DD HH:mm[:ss]` (hora local) → Date. */
export function parseDateTime(value: DateTime): Date {
  const [date, time = '00:00'] = value.split(' ');
  const [y, m, d] = date.split('-').map(Number);
  const [h = 0, min = 0, s = 0] = time.split(':').map(Number);
  return new Date(y, m - 1, d, h, min, s);
}

/** `2026-09-26 13:58` → `26/09/2026 13:58`. */
export function formatDateTimeShort(value: DateTime): string {
  const [date, time = ''] = value.split(' ');
  return `${formatDMY(date)}${time ? ` ${time.slice(0, 5)}` : ''}`;
}

/** Último acceso: fecha y hora, o "Nunca" si no ha iniciado sesión. */
export function formatLastAccess(value: DateTime | null): string {
  return value ? formatDateTimeShort(value) : 'Nunca';
}

/** "Hace 2 min", "Hace 6 h", "Hace 1 día"… (más de 30 días → fecha). */
export function formatRelative(value: DateTime, now = new Date()): string {
  const minutes = Math.max(0, Math.round((now.getTime() - parseDateTime(value).getTime()) / 60_000));
  if (minutes < 1) return 'Hace un momento';
  if (minutes < 60) return `Hace ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `Hace ${hours} h`;
  const days = Math.floor(hours / 24);
  if (days <= 30) return days === 1 ? 'Hace 1 día' : `Hace ${days} días`;
  return formatDateTimeShort(value);
}

/** Días transcurridos desde una fecha ISO hasta hoy. */
export function daysSince(iso: ISODate): number {
  return Math.max(0, diffDays(iso, todayISO()));
}

/** "1 intento" / "3 intentos". */
export function plural(n: number, singular: string, pluralForm = `${singular}s`): string {
  return `${n} ${n === 1 ? singular : pluralForm}`;
}
