import type { AuditAction, AuditEvent, AuditFilters, AuditResult } from '../../types';
import type { DateRangePreset } from '../../components/shared';
import { filterAuditEvents } from '../../services';
import { addDays, formatDM, formatDMY, todayISO } from '../../utils/dates';

/** Opciones de "Filas por página" del registro. */
export const AUDIT_PAGE_SIZES = [25, 50, 100];

const MAX_LOOKBACK_DAYS = 365;

/** Rangos rápidos del canvas: Hoy · 7 días · 30 días. */
export function auditPresets(today = todayISO()): DateRangePreset[] {
  return [
    { label: 'Hoy', range: { from: today, to: today } },
    { label: '7 días', range: { from: addDays(today, -6), to: today } },
    { label: '30 días', range: { from: addDays(today, -29), to: today } },
  ];
}

/** Fecha mínima seleccionable. */
export const auditMinDate = (today = todayISO()) => addDays(today, -MAX_LOOKBACK_DAYS);

/** Filtros iniciales: últimos 7 días, sin más restricciones. */
export function defaultAuditFilters(today = todayISO()): AuditFilters {
  return { q: '', from: addDays(today, -6), to: today, usuario: '', accion: '', resultado: '' };
}

export const isDefaultRange = (f: AuditFilters, d: AuditFilters) => f.from === d.from && f.to === d.to;

/** Filtros secundarios activos (periodo distinto del predeterminado, usuario, acción, resultado). */
export function countActiveFilters(f: AuditFilters, d: AuditFilters): number {
  return (isDefaultRange(f, d) ? 0 : 1) + (f.usuario ? 1 : 0) + (f.accion ? 1 : 0) + (f.resultado ? 1 : 0);
}

export const hasAnyFilter = (f: AuditFilters, d: AuditFilters) => countActiveFilters(f, d) > 0 || f.q.trim() !== '';

/** Etiqueta del periodo: el nombre del rango rápido si coincide, si no "dd/mm – dd/mm". */
export function rangeLabel(f: AuditFilters, presets: DateRangePreset[]): string {
  const preset = presets.find((p) => p.range.from === f.from && p.range.to === f.to);
  if (preset) return preset.label;
  return f.from === f.to ? formatDMY(f.from) : `${formatDM(f.from)} – ${formatDM(f.to)}`;
}

/** Aplica los filtros de la vista. */
export function applyAuditFilters(events: AuditEvent[], f: AuditFilters): AuditEvent[] {
  const q = f.q.trim().toLowerCase();
  const base = filterAuditEvents(events, { ...f, q: '' });
  if (!q) return base;
  return base.filter((e) =>
    `${e.id} ${e.usuario} ${e.email} ${e.rol} ${e.accion} ${e.recurso} ${e.ip} ${e.descripcion} ${e.error ?? ''}`.toLowerCase().includes(q),
  );
}

/** Conteos por resultado (para el filtro segmentado), ignorando el filtro de resultado actual. */
export function resultCounts(events: AuditEvent[], f: AuditFilters): Record<AuditResult | '', number> {
  const base = applyAuditFilters(events, { ...f, resultado: '' });
  const ok = base.filter((e) => e.resultado === 'Éxito').length;
  return { '': base.length, Éxito: ok, Error: base.length - ok };
}

/** `YYYY-MM-DD HH:mm:ss` → `dd/mm/yyyy HH:mm` (o con segundos). */
export function formatAuditTimestamp(ts: string, withSeconds = false): string {
  const date = formatDMY(ts.slice(0, 10));
  const time = ts.slice(11, withSeconds ? 19 : 16);
  return `${date} ${time}`;
}

/** Opciones del filtro de acción. */
export const actionOptions = (actions: AuditAction[]) => actions.map((a) => ({ value: a, label: a }));
