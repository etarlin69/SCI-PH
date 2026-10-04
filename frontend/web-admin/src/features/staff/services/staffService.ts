import { ASSIGNMENTS_MOCK, LICENSES_MOCK, STAFF_HISTORY_MOCK, STAFF_MOCK } from '../../../data/mock';
import type {
  Assignment,
  AssignmentInput,
  ISODate,
  License,
  LicenseInput,
  LicenseStatus,
  StaffAccessInput,
  StaffHistoryEntry,
  StaffInput,
  StaffMember,
} from '../../../types';
import { ServiceError } from '../../../services/core/errors';
import { mockList, mockRead, mockWrite, nextId, notFound } from '../../../services/core/mock';
import { record } from '../../audit/services/auditService';
import { invitationsService } from '../../invitations/services/invitationsService';
import { usersService } from '../../users/services/usersService';

const staff = STAFF_MOCK;
const assignments: Assignment[] = [...ASSIGNMENTS_MOCK];
const licenses: License[] = [...LICENSES_MOCK];
const history: StaffHistoryEntry[] = [...STAFF_HISTORY_MOCK];

/** Días antes del vencimiento en que una licencia pasa a "Por vencer". */
export const LICENSE_WARNING_DAYS = 30;

const todayISO = () => new Date().toISOString().slice(0, 10);

/** Estado de una licencia según su vencimiento (pura: también la usa la UI). */
export function licenseStatus(vence: ISODate | undefined, today: ISODate = todayISO()): { status: LicenseStatus; days: number | null } {
  if (!vence) return { status: 'Vigente', days: null };
  const days = Math.round((Date.parse(vence) - Date.parse(today)) / 86_400_000);
  return { status: days < 0 ? 'Vencida' : days <= LICENSE_WARNING_DAYS ? 'Por vencer' : 'Vigente', days };
}

function find(id: string): StaffMember {
  return staff.find((s) => s.id === id) ?? notFound('La persona');
}

function log(staffId: string, titulo: string, tone: StaffHistoryEntry['tone'] = 'info'): void {
  history.unshift({ id: `H-${Date.now()}`, staffId, fecha: todayISO(), titulo, detalle: 'por Admin SCIH', tone });
}

export const staffService = {
  list(): Promise<StaffMember[]> {
    return mockList(() => staff);
  },

  get(id: string): Promise<StaffMember> {
    return mockRead(() => find(id));
  },

  /** Alta del perfil laboral. */
  async create(input: StaffInput, access: StaffAccessInput): Promise<StaffMember> {
    if (staff.some((s) => s.email.toLowerCase() === input.email.trim().toLowerCase())) {
      throw new ServiceError('CONFLICT', 'Ya hay personal registrado con este email.', { field: 'email' });
    }
    let invitationId: string | undefined;
    if (access.invitar && access.rol) {
      const inv = await invitationsService.inviteStaff({ email: input.email, rol: access.rol, departamento: input.departamento });
      invitationId = inv.id;
    }
    return mockWrite('staff.create', () => {
      const member: StaffMember = {
        ...input,
        nombre: input.nombre.trim(),
        email: input.email.trim().toLowerCase(),
        id: nextId(
          'PER-',
          staff.map((s) => s.id),
          4,
        ),
        cuenta: access.invitar ? { estado: 'pendiente', invitationId } : { estado: 'sin_acceso' },
        fechaIngreso: todayISO(),
      };
      staff.push(member);
      log(member.id, `Alta en Personal · ${member.puesto}`, 'success');
      record({ accion: 'Crear', recurso: `Personal ${member.id}`, descripcion: `Alta de ${member.nombre} (${member.puesto}).` });
      return member;
    });
  },

  update(id: string, input: StaffInput): Promise<StaffMember> {
    return mockWrite('staff.update', () => {
      const member = find(id);
      if (staff.some((s) => s.id !== id && s.email.toLowerCase() === input.email.trim().toLowerCase())) {
        throw new ServiceError('CONFLICT', 'Ya hay personal registrado con este email.', { field: 'email' });
      }
      if (member.clinicId !== input.clinicId) log(id, 'Traslado de clínica');
      if (member.estado !== input.estado) log(id, `Estado cambiado a «${input.estado}»`, 'warning');
      if (member.puesto !== input.puesto) log(id, `Puesto cambiado a ${input.puesto}`);
      Object.assign(member, { ...input, nombre: input.nombre.trim(), email: input.email.trim().toLowerCase() });
      record({ accion: 'Actualizar', recurso: `Personal ${id}`, descripcion: 'Perfil laboral actualizado.' });
      return member;
    });
  },

  /** Elimina el perfil; opcionalmente suspende la cuenta de usuario vinculada. */
  async remove(id: string, options: { suspendAccount: boolean }): Promise<void> {
    const member = find(id);
    if (options.suspendAccount && member.cuenta.estado === 'vinculada') {
      await usersService.setStatus(member.cuenta.userId, 'Suspendido');
    }
    return mockWrite('staff.remove', () => {
      staff.splice(staff.indexOf(member), 1);
      record({ accion: 'Eliminar', recurso: `Personal ${id}`, descripcion: `Perfil de ${member.nombre} eliminado.` });
    });
  },

  listAssignments(staffId: string): Promise<Assignment[]> {
    return mockList(() => assignments.filter((a) => a.staffId === staffId).sort((a, b) => b.desde.localeCompare(a.desde)));
  },

  addAssignment(staffId: string, input: AssignmentInput): Promise<Assignment> {
    return mockWrite('staff.assignment', () => {
      find(staffId);
      const a: Assignment = { ...input, id: `ASG-${Date.now()}`, staffId };
      assignments.push(a);
      log(staffId, `Asignado a ${a.lugar} · ${a.turno}`);
      record({ accion: 'Crear', recurso: `Personal ${staffId}`, descripcion: `Nueva asignación: ${a.lugar}.` });
      return a;
    });
  },

  listLicenses(staffId: string): Promise<License[]> {
    return mockList(() => licenses.filter((l) => l.staffId === staffId));
  },

  /** Añade una licencia o, si `replaceId`, registra su renovación. */
  saveLicense(staffId: string, input: LicenseInput, replaceId?: string): Promise<License> {
    return mockWrite('staff.license', () => {
      find(staffId);
      if (input.vence && input.vence <= todayISO()) {
        throw new ServiceError('VALIDATION', 'La fecha de vencimiento debe ser futura.', { field: 'vence' });
      }
      const existing = replaceId ? licenses.find((l) => l.id === replaceId) : undefined;
      if (existing) {
        Object.assign(existing, input);
        log(staffId, `Renovación registrada: ${input.tipo.split(' · ')[0]}`, 'success');
        return existing;
      }
      const lic: License = { ...input, id: `LIC-${Date.now()}`, staffId };
      licenses.push(lic);
      log(staffId, `Licencia añadida: ${input.tipo.split(' · ')[0]}`, 'success');
      return lic;
    });
  },

  listHistory(staffId: string): Promise<StaffHistoryEntry[]> {
    return mockList(() => history.filter((h) => h.staffId === staffId));
  },
};
