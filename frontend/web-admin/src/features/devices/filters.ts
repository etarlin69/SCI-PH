import type { Device, DeviceFilters, DeviceStatus } from '../../types';
import { DEVICE_STATUSES } from './helpers';

export const EMPTY_DEVICE_FILTERS: DeviceFilters = { q: '', tipo: '', estado: '' };

/** Búsqueda por nombre, ID o asignación + filtros de tipo y estado. */
export function filterDevices(devices: Device[], f: DeviceFilters): Device[] {
  const q = f.q.trim().toLowerCase();
  return devices.filter(
    (d) =>
      (!q || `${d.nombre} ${d.id} ${d.asignadoA}`.toLowerCase().includes(q)) &&
      (!f.tipo || d.tipo === f.tipo) &&
      (!f.estado || d.estado === f.estado),
  );
}

/** Contadores por estado para el filtro segmentado (`''` = todos). */
export function countDevicesByStatus(devices: Device[]): Record<DeviceStatus | '', number> {
  const counts = { '': devices.length } as Record<DeviceStatus | '', number>;
  DEVICE_STATUSES.forEach((s) => {
    counts[s] = devices.filter((d) => d.estado === s).length;
  });
  return counts;
}

export function hasDeviceFilters(f: DeviceFilters): boolean {
  return !!(f.q.trim() || f.tipo || f.estado);
}

/** Filtros que en móvil viven en la hoja inferior (para el contador "Filtros (n)"). */
export function countSecondaryDeviceFilters(f: DeviceFilters): number {
  return f.tipo ? 1 : 0;
}
