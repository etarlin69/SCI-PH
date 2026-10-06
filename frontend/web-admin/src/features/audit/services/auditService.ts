import { AUDIT_ACTIONS, AUDIT_EVENTS_MOCK, AUDIT_USERS } from '../../../data/mock';
import type { AuditEvent, AuditExportFormat, AuditFilters, AuditRecordInput } from '../../../types';
import { downloadFile, toCsv } from '../../../utils/csv';
import { getActor } from '../../../services/core/actor';
import { mockList, mockRead, mockWrite, nowTimestamp } from '../../../services/core/mock';

const log: AuditEvent[] = [...AUDIT_EVENTS_MOCK];
let seq = 0;

/** Aplica los filtros de la vista (búsqueda global, rango, usuario, acción, resultado). */
export function filterAuditEvents(events: AuditEvent[], f: AuditFilters): AuditEvent[] {
  const q = f.q.trim().toLowerCase();
  return events.filter((e) => {
    const day = e.timestamp.slice(0, 10);
    if (day < f.from || day > f.to) return false;
    if (f.usuario && e.usuario !== f.usuario) return false;
    if (f.accion && e.accion !== f.accion) return false;
    if (f.resultado && e.resultado !== f.resultado) return false;
    if (q && !`${e.id} ${e.usuario} ${e.accion} ${e.recurso} ${e.ip} ${e.error ?? ''}`.toLowerCase().includes(q)) return false;
    return true;
  });
}

export const auditService = {
  list(): Promise<AuditEvent[]> {
    return mockList(() => log);
  },

  get(id: string): Promise<AuditEvent | undefined> {
    return mockRead(() => log.find((e) => e.id === id));
  },

  /** Últimos eventos de un usuario (detalle de usuario → "Actividad reciente"). */
  listByUser(email: string, limit = 5): Promise<AuditEvent[]> {
    return mockList(() => log.filter((e) => e.email === email).slice(0, limit));
  },

  facets(): Promise<{ usuarios: string[]; acciones: typeof AUDIT_ACTIONS }> {
    return mockRead(() => ({ usuarios: AUDIT_USERS, acciones: AUDIT_ACTIONS }));
  },

  /** Exporta los eventos filtrados. */
  async export(format: AuditExportFormat, events: AuditEvent[], filters: AuditFilters): Promise<{ filename: string; count: number }> {
    const filename = `auditoria_${filters.from}_${filters.to}.${format}`;
    await mockWrite(`audit.export.${format}`, () => undefined);
    if (format === 'csv') {
      downloadFile(
        filename,
        toCsv([
          ['ID', 'Fecha/hora', 'Usuario', 'Rol', 'Acción', 'Recurso', 'Resultado', 'Error', 'IP', 'Hash'],
          ...events.map((e) => [e.id, e.timestamp, e.usuario, e.rol, e.accion, e.recurso, e.resultado, e.error ?? '', e.ip, e.hash]),
        ]),
      );
    }
    record({
      accion: 'Exportar',
      recurso: `Reporte de auditoría (${format.toUpperCase()})`,
      descripcion: `Exportación de ${events.length} eventos con filtros aplicados.`,
    });
    return { filename, count: events.length };
  },
};

/** Anexa un evento (lo usan los demás servicios mock tras cada operación). */
export function record(input: AuditRecordInput): void {
  const actor = getActor();
  seq += 1;
  log.unshift({
    id: `EVT-${88413 + seq}`,
    timestamp: nowTimestamp(),
    usuario: actor.nombre,
    email: actor.email,
    rol: actor.rol,
    accion: input.accion,
    recurso: input.recurso,
    resultado: input.resultado ?? 'Éxito',
    error: input.error,
    descripcion: input.descripcion,
    ip: '10.10.1.2',
    userAgent: typeof navigator === 'undefined' ? '' : navigator.userAgent,
    cambios: input.cambios ?? [],
    hash: `sha256:${Math.random().toString(16).slice(2, 8)}…${Math.random().toString(16).slice(2, 6)}`,
  });
}
