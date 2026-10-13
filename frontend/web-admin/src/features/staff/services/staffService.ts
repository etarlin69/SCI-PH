import { apiRequest } from '../../../api';
import type {
  ClinicStaffAffiliation,
  ClinicalStaffAdmin,
  RegisterClinicalStaffRequest,
  StaffListQuery,
} from '../types/staff.types';

const BASE = '/api/web/admin/staff';
const id = (value: string) => encodeURIComponent(value);

/**
 * Personal clínico y sus afiliaciones (permiso ClinicalStaff.Manage). Alta, activar/desactivar, afiliar y terminar
 * afiliaciones; no hay edición ni borrado y el historial se conserva. La cuenta se crea con la invitación de personal
 * existente (invitationsService.inviteStaff, Users.Manage).
 */
export const staffService = {
  list: ({ search, status }: StaffListQuery = {}, signal?: AbortSignal) =>
    apiRequest<ClinicalStaffAdmin[]>(BASE, { query: { search: search?.trim() || undefined, status }, signal }),

  get: (staffId: string, signal?: AbortSignal) => apiRequest<ClinicalStaffAdmin>(`${BASE}/${id(staffId)}`, { signal }),

  register: (request: RegisterClinicalStaffRequest) => apiRequest<ClinicalStaffAdmin>(BASE, { method: 'POST', body: request }),

  activate: (staffId: string) => apiRequest<void>(`${BASE}/${id(staffId)}/activate`, { method: 'POST' }),

  deactivate: (staffId: string) => apiRequest<void>(`${BASE}/${id(staffId)}/deactivate`, { method: 'POST' }),

  listAffiliations: (staffId: string, signal?: AbortSignal) =>
    apiRequest<ClinicStaffAffiliation[]>(`${BASE}/${id(staffId)}/affiliations`, { signal }),

  /** Abre una afiliación ahora con una clínica activa (409 si ya hay una abierta o algo está inactivo). */
  startAffiliation: (staffId: string, clinicId: string) =>
    apiRequest<ClinicStaffAffiliation>(`${BASE}/${id(staffId)}/affiliations`, { method: 'POST', body: { clinicId } }),

  /** Termina la afiliación ahora; queda como historial. */
  endAffiliation: (staffId: string, affiliationId: string) =>
    apiRequest<ClinicStaffAffiliation>(`${BASE}/${id(staffId)}/affiliations/${id(affiliationId)}/end`, { method: 'POST' }),
};
