import type { DateTime, DeviceStatus, DeviceType, ISODate } from '../../types';

export type DeviceAction = 'rotate' | 'revoke' | 'delete';

export const DEVICE_TYPES: DeviceType[] = ['Tablet', 'Móvil', 'Escritorio'];
export const DEVICE_STATUSES: DeviceStatus[] = ['Activo', 'Pendiente', 'Revocado'];

/** Orden lógico de estados para ordenar la tabla. */
export const DEVICE_STATUS_ORDER: Record<DeviceStatus, number> = { Activo: 0, Pendiente: 1, Revocado: 2 };

const pad = (n: number) => String(n).padStart(2, '0');

/** `YYYY-MM-DD[ HH:mm]` → Date local. */
export function parseDateTime(value: DateTime | ISODate): Date {
  const [date, time = '00:00'] = value.split(' ');
  const [y, m, d] = date.split('-').map(Number);
  const [hh, mm] = time.split(':').map(Number);
  return new Date(y, m - 1, d, hh, mm);
}

/** `dd/mm/yyyy HH:mm`. */
export function formatDateTime(value: DateTime): string {
  const d = parseDateTime(value);
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** `dd/mm/yyyy`. */
export function formatDate(value: DateTime | ISODate): string {
  return formatDateTime(value.length === 10 ? `${value} 00:00` : value).slice(0, 10);
}

/** "Hace 9 min", "Hace 3 h", "Hace 1 día", "Hace 12 días". */
export function relativeTime(value: DateTime, now = Date.now()): string {
  const min = Math.round((now - parseDateTime(value).getTime()) / 60_000);
  if (min < 60) return `Hace ${Math.max(1, min)} min`;
  if (min < 1440) return `Hace ${Math.round(min / 60)} h`;
  const days = Math.round(min / 1440);
  return `Hace ${days} ${days === 1 ? 'día' : 'días'}`;
}

/** Días transcurridos desde la emisión de la credencial. */
export function credentialAgeDays(emitidaEl: ISODate, now = Date.now()): number {
  return Math.max(0, Math.floor((now - parseDateTime(emitidaEl).getTime()) / 86_400_000));
}

/** Secreto en grupos de 4 caracteres para leerlo/teclearlo sin errores. */
export function groupSecret(secret: string): string {
  return secret.replace(/(.{4})/g, '$1 ').trim();
}

/** Segundos → `m:ss`. */
export function formatCountdown(seconds: number): string {
  return `${Math.floor(seconds / 60)}:${pad(seconds % 60)}`;
}

/** Motivo por el que no se puede rotar la credencial (o `undefined` si se puede). */
export function rotateBlockedReason(estado: DeviceStatus): string | undefined {
  if (estado === 'Pendiente') return 'Disponible tras la primera vinculación';
  if (estado === 'Revocado') return 'No disponible: el dispositivo está revocado';
  return undefined;
}
