import type { StaffFilters, StaffMember, StaffStatus } from '../../types';

export const STAFF_STATUSES: StaffStatus[] = ['Activo', 'De licencia', 'Inactivo'];
export const EMPTY_STAFF_FILTERS: StaffFilters = { q: '', puesto: '', clinicId: '', estado: '' };

export const STATUS_HINT: Record<StaffStatus, string> = {
  Activo: 'Disponible para turnos y asignaciones.',
  'De licencia': 'No se le asignarán turnos mientras dure.',
  Inactivo: 'No aparecerá en turnos ni asignaciones.',
};

export function filterStaff(list: StaffMember[], f: StaffFilters): StaffMember[] {
  const q = f.q.trim().toLowerCase();
  return list.filter(
    (s) =>
      (!q || `${s.nombre} ${s.email} ${s.id}`.toLowerCase().includes(q)) &&
      (!f.puesto || s.puesto === f.puesto) &&
      (!f.clinicId || s.clinicId === f.clinicId) &&
      (!f.estado || s.estado === f.estado),
  );
}

export const activeStaffFilters = (f: StaffFilters) => [f.q, f.puesto, f.clinicId, f.estado].filter(Boolean).length;
