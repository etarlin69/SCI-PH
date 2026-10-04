import type { ISODate } from '../types';

export const MONTHS_ES = [
  'enero',
  'febrero',
  'marzo',
  'abril',
  'mayo',
  'junio',
  'julio',
  'agosto',
  'septiembre',
  'octubre',
  'noviembre',
  'diciembre',
];

/** Semana empezando en lunes. */
export const WEEKDAYS_ES = ['lu', 'ma', 'mi', 'ju', 'vi', 'sá', 'do'];

const pad = (n: number) => String(n).padStart(2, '0');

export function toISO(d: Date): ISODate {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** Convierte `YYYY-MM-DD` a Date a medianoche local (evita el desfase UTC de `new Date(iso)`). */
export function parseISO(iso: ISODate): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d);
}

export function todayISO(): ISODate {
  return toISO(new Date());
}

export function addDays(iso: ISODate, days: number): ISODate {
  const d = parseISO(iso);
  d.setDate(d.getDate() + days);
  return toISO(d);
}

/** Días enteros de `a` a `b` (positivo si `b` es posterior). */
export function diffDays(a: ISODate, b: ISODate): number {
  return Math.round((parseISO(b).getTime() - parseISO(a).getTime()) / 86_400_000);
}

export function startOfMonth(iso: ISODate): ISODate {
  return `${iso.slice(0, 7)}-01`;
}

/** Las fechas ISO se comparan correctamente como texto. */
export function isWithin(iso: ISODate, from: ISODate, to: ISODate): boolean {
  return iso >= from && iso <= to;
}

export function formatDMY(iso: ISODate): string {
  const [y, m, d] = iso.split('-');
  return `${d}/${m}/${y}`;
}

export function formatDM(iso: ISODate): string {
  const [, m, d] = iso.split('-');
  return `${d}/${m}`;
}

/** "20 de septiembre de 2026" — para aria-labels. */
export function formatLong(iso: ISODate): string {
  const [y, m, d] = iso.split('-').map(Number);
  return `${d} de ${MONTHS_ES[m - 1]} de ${y}`;
}

export function monthLabel(year: number, month: number): string {
  const name = MONTHS_ES[month];
  return `${name.charAt(0).toUpperCase()}${name.slice(1)} ${year}`;
}

/** `dd/mm/yyyy HH:MM:SS` en hora local. */
export function formatDateTime(d: Date): string {
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

export interface CalendarCell {
  iso: ISODate;
  day: number;
}

/** Celdas de un mes para una cuadrícula que empieza en lunes. */
export function monthGrid(year: number, month: number): (CalendarCell | null)[] {
  const first = new Date(year, month, 1);
  const lead = (first.getDay() + 6) % 7;
  const days = new Date(year, month + 1, 0).getDate();
  const cells: (CalendarCell | null)[] = Array.from({ length: lead }, () => null);
  for (let day = 1; day <= days; day++) {
    cells.push({ iso: toISO(new Date(year, month, day)), day });
  }
  return cells;
}

/** `"2026-09-26 14:05"` (o con segundos) → `"26/09/2026 14:05"`. */
export function formatDateTimeText(dt: string | null | undefined): string {
  if (!dt) return '—';
  const [date, time = ''] = dt.split(' ');
  return `${formatDMY(date)}${time ? ` ${time.slice(0, 5)}` : ''}`;
}

/** Fecha ISO o DateTime → "hace 5 min", "hace 3 h", "hace 2 días", "26/09/2026". */
export function formatRelative(dt: string | null | undefined, now = new Date()): string {
  if (!dt) return 'Nunca';
  const [date, time = '00:00'] = dt.split(' ');
  const d = parseISO(date);
  const [h, m] = time.split(':').map(Number);
  d.setHours(h || 0, m || 0);
  const mins = Math.round((now.getTime() - d.getTime()) / 60_000);
  if (mins < 1) return 'Ahora mismo';
  if (mins < 60) return `Hace ${mins} min`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `Hace ${hours} h`;
  const days = Math.round(hours / 24);
  if (days < 30) return `Hace ${days} ${days === 1 ? 'día' : 'días'}`;
  return formatDMY(date);
}
