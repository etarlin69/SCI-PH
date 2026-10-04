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

export const WEEKDAYS_ES = ['lu', 'ma', 'mi', 'ju', 'vi', 'sá', 'do'];

const pad = (n: number) => String(n).padStart(2, '0');

export function toISO(d: Date): ISODate {
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

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

export function diffDays(a: ISODate, b: ISODate): number {
    return Math.round((parseISO(b).getTime() - parseISO(a).getTime()) / 86_400_000);
}

export function startOfMonth(iso: ISODate): ISODate {
    return `${iso.slice(0, 7)}-01`;
}

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

export function formatLong(iso: ISODate): string {
    const [y, m, d] = iso.split('-').map(Number);
    return `${d} de ${MONTHS_ES[m - 1]} de ${y}`;
}

export function monthLabel(year: number, month: number): string {
    const name = MONTHS_ES[month];
    return `${name.charAt(0).toUpperCase()}${name.slice(1)} ${year}`;
}

export function formatDateTime(d: Date): string {
    return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

export interface CalendarCell {
    iso: ISODate;
    day: number;
}

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
