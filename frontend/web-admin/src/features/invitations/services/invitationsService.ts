import { INVITATIONS_MOCK, USERS_MOCK } from '../../../data/mock';
import type { Invitation, InvitePatientInput, InviteStaffInput } from '../../../types';
import { getActor } from '../../../services/core/actor';
import { ServiceError } from '../../../services/core/errors';
import { mockList, mockRead, mockWrite, nextId, notFound, nowDateTime } from '../../../services/core/mock';
import { record } from '../../audit/services/auditService';

/** Días de validez del enlace de invitación. */
export const INVITATION_TTL_DAYS = 7;

const invitations: Invitation[] = [...INVITATIONS_MOCK];

function find(id: string): Invitation {
  return invitations.find((i) => i.id === id) ?? notFound('La invitación');
}

function assertCanInvite(email: string, staff: boolean): string {
  const em = email.trim().toLowerCase();
  const pending = invitations.find((i) => i.email.toLowerCase() === em && i.estado === 'Pendiente');
  if (pending) {
    throw new ServiceError('CONFLICT', `Ya hay una invitación pendiente para este email (${pending.id}). Reenvíala desde la lista.`, {
      field: 'email',
      invitationId: pending.id,
    });
  }
  if (staff && USERS_MOCK.some((u) => u.email.toLowerCase() === em)) {
    throw new ServiceError('CONFLICT', 'Ya existe un usuario con este email.', { field: 'email' });
  }
  return em;
}

function create(data: Omit<Invitation, 'id' | 'estado' | 'enviadaEl' | 'enviadaPor' | 'reenvios'>): Invitation {
  const inv: Invitation = {
    ...data,
    id: nextId(
      'INV-',
      invitations.map((i) => i.id),
      4,
    ),
    estado: 'Pendiente',
    enviadaEl: nowDateTime(),
    enviadaPor: getActor().nombre,
    reenvios: 0,
  };
  invitations.unshift(inv);
  record({
    accion: 'Crear',
    recurso: `Invitación ${inv.id}`,
    descripcion: `Invitación de ${inv.tipo.toLowerCase()} enviada a ${inv.email}.`,
    cambios: [{ campo: 'Estado', antes: '—', despues: 'Pendiente' }],
  });
  return inv;
}

export const invitationsService = {
  list(): Promise<Invitation[]> {
    return mockList(() => invitations);
  },

  get(id: string): Promise<Invitation> {
    return mockRead(() => find(id));
  },

  invitePatient(input: InvitePatientInput): Promise<Invitation> {
    return mockWrite('invitations.patient', () => {
      const email = assertCanInvite(input.email, false);
      return create({ email, tipo: 'Paciente', mensaje: input.mensaje?.trim() || undefined });
    });
  },

  inviteStaff(input: InviteStaffInput): Promise<Invitation> {
    return mockWrite('invitations.staff', () => {
      const email = assertCanInvite(input.email, true);
      return create({
        email,
        tipo: 'Personal',
        rol: input.rol,
        departamento: input.departamento,
        mensaje: input.mensaje?.trim() || undefined,
      });
    });
  },

  /** Reenvía con un enlace nuevo (el anterior deja de funcionar) y reinicia los 7 días. */
  reissue(id: string): Promise<Invitation> {
    return mockWrite('invitations.reissue', () => {
      const inv = find(id);
      if (inv.estado === 'Aceptada') throw new ServiceError('CONFLICT', 'La invitación ya fue aceptada.');
      const antes = inv.estado;
      Object.assign(inv, { estado: 'Pendiente', enviadaEl: nowDateTime(), reenvios: inv.reenvios + 1, enviadaPor: getActor().nombre });
      record({
        accion: 'Actualizar',
        recurso: `Invitación ${id}`,
        descripcion: `Invitación reenviada a ${inv.email}.`,
        cambios: [{ campo: 'Estado', antes, despues: 'Pendiente' }],
      });
      return inv;
    });
  },

  revoke(id: string): Promise<Invitation> {
    return mockWrite('invitations.revoke', () => {
      const inv = find(id);
      if (inv.estado !== 'Pendiente') throw new ServiceError('CONFLICT', 'Solo se pueden revocar invitaciones pendientes.');
      inv.estado = 'Revocada';
      record({
        accion: 'Revocar',
        recurso: `Invitación ${id}`,
        descripcion: `Invitación a ${inv.email} revocada.`,
        cambios: [{ campo: 'Estado', antes: 'Pendiente', despues: 'Revocada' }],
      });
      return inv;
    });
  },
};
