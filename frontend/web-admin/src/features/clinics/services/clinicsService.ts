import { ApiError, apiRequest, describeApiError } from '../../../api';
import type { ClinicAdmin, ClinicListQuery, RegisterClinicRequest } from '../types/clinics.types';

const BASE = '/api/web/admin/clinics';
const id = (value: string) => encodeURIComponent(value);

/**
 * Clínicas de la red (permiso Clinics.Manage). Solo lo que admite el Domain: alta, activar y desactivar.
 * Desactivar no termina las afiliaciones: la clínica deja de ser un contexto clínico válido.
 */
export const clinicsService = {
  list: ({ search, status }: ClinicListQuery = {}, signal?: AbortSignal) =>
    apiRequest<ClinicAdmin[]>(BASE, { query: { search: search?.trim() || undefined, status }, signal }),

  get: (clinicId: string, signal?: AbortSignal) => apiRequest<ClinicAdmin>(`${BASE}/${id(clinicId)}`, { signal }),

  register: (request: RegisterClinicRequest) => apiRequest<ClinicAdmin>(BASE, { method: 'POST', body: request }),

  activate: (clinicId: string) => apiRequest<void>(`${BASE}/${id(clinicId)}/activate`, { method: 'POST' }),

  deactivate: (clinicId: string) => apiRequest<void>(`${BASE}/${id(clinicId)}/deactivate`, { method: 'POST' }),
};

// Mensajes conocidos (ProblemDetails.detail, en inglés) de la administración clínica, traducidos para la UI.
const KNOWN_DETAILS: [RegExp, string][] = [
  [/facility code already exists/i, 'Ya existe una clínica con ese código de establecimiento.'],
  [/professional license number already exists/i, 'Ya existe personal con ese número de licencia.'],
  [/account is already linked/i, 'La cuenta ya está vinculada a otro miembro del personal.'],
  [/already has an open affiliation/i, 'Ya tiene una afiliación abierta con esa clínica.'],
  [/inactive clinic/i, 'No se puede afiliar personal a una clínica inactiva.'],
  [/inactive staff member/i, 'No se puede afiliar a un miembro del personal inactivo.'],
  [/affiliation has already ended/i, 'La afiliación ya estaba finalizada.'],
];

/** Mensaje legible de un error de la administración de clínicas o personal (sin datos sensibles). */
export function describeOrganizationError(error: unknown): string {
  const detail = error instanceof ApiError ? error.problem?.detail : undefined;
  const known = typeof detail === 'string' ? KNOWN_DETAILS.find(([pattern]) => pattern.test(detail)) : undefined;
  if (known) return known[1];
  return describeApiError(error, { 404: 'El registro no existe.', 409: 'La operación entra en conflicto con el estado actual.' });
}
