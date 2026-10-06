import { apiRequest, describeApiError } from '../../../api';
import type { InvitationSummary, InvitePatientRequest, InviteStaffRequest } from '../types/invitations.types';

const BASE = '/api/web/admin/invitations';

/**
 * Invitaciones de cuenta. El backend aún no ofrece GET de lista ni de detalle: solo crear, reemitir y revocar.
 * Invitar paciente exige Accounts.InvitePatient; el resto, Users.Manage.
 */
export const invitationsService = {
  invitePatient: (request: InvitePatientRequest) => apiRequest<InvitationSummary>(`${BASE}/patients`, { method: 'POST', body: request }),

  inviteStaff: (request: InviteStaffRequest) => apiRequest<InvitationSummary>(`${BASE}/staff`, { method: 'POST', body: request }),

  /** Emite un enlace nuevo (invalida el anterior) y devuelve la invitación resultante. */
  reissue: (id: string) => apiRequest<InvitationSummary>(`${BASE}/${encodeURIComponent(id)}/reissue`, { method: 'POST' }),

  revoke: (id: string) => apiRequest<void>(`${BASE}/${encodeURIComponent(id)}/revoke`, { method: 'POST' }),
};

/** Mensajes de InvitationsController.ToResult. */
export const describeInvitationError = (e: unknown) =>
  describeApiError(e, {
    400: 'Datos no válidos: revisa el rol (solo personal clínico o recepción) o que el paciente tenga un email activo.',
    404: 'No se encontró el registro o la invitación indicada.',
    409: 'El registro ya tiene cuenta, el email está en uso o la invitación ya no está pendiente.',
  });

export const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
