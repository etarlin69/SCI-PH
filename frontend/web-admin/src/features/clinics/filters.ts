import type { Clinic, ClinicStatus, ClinicType } from '../../types';

/** Tipos de centro disponibles (mismo orden que el formulario del canvas). */
export const CLINIC_TYPES: ClinicType[] = ['Hospital', 'Clínica', 'Centro de diagnóstico'];

export const CLINIC_STATUSES: ClinicStatus[] = ['Activa', 'Inactiva'];

export interface ClinicListFilters {
  q: string;
  provincia: string;
  tipo: ClinicType | '';
  estado: ClinicStatus | '';
}

export const EMPTY_CLINIC_FILTERS: ClinicListFilters = { q: '', provincia: '', tipo: '', estado: '' };

/** Normaliza para buscar sin distinguir mayúsculas ni tildes. */
export function normalize(text: string): string {
  return text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}

/** Aplica los filtros (la búsqueda cubre nombre, dirección e id). */
export function filterClinics(list: Clinic[], f: ClinicListFilters): Clinic[] {
  const q = normalize(f.q);
  return list.filter(
    (c) =>
      (!q || normalize(`${c.nombre} ${c.direccion} ${c.id} ${c.contacto}`).includes(q)) &&
      (!f.provincia || c.provincia === f.provincia) &&
      (!f.tipo || c.tipo === f.tipo) &&
      (!f.estado || c.estado === f.estado),
  );
}

/** Filtros secundarios activos (los que van en la hoja "Filtros (n)" en móvil). */
export function countSecondaryFilters(f: ClinicListFilters): number {
  return [f.provincia, f.tipo].filter(Boolean).length;
}

export function hasActiveFilters(f: ClinicListFilters): boolean {
  return !!(f.q.trim() || f.provincia || f.tipo || f.estado);
}
