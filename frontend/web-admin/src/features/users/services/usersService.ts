import { SESSIONS_MOCK, USERS_MOCK, securityFor } from '../../../data/mock';
import type { AuditEvent, CreateUserInput, UpdateUserInput, User, UserSecuritySummary, UserSession, UserStatus } from '../../../types';
import { ServiceError } from '../../../services/core/errors';
import { mockList, mockRead, mockWrite, nextId, notFound } from '../../../services/core/mock';
import { auditService, record } from '../../audit/services/auditService';
import { rolesService } from '../../roles/services/rolesService';

const users = USERS_MOCK;
let sessions: UserSession[] = [...SESSIONS_MOCK];
const security = new Map<string, UserSecuritySummary>();

/** Id del usuario que tiene la sesión (no puede suspenderse ni eliminarse a sí mismo). */
export const SELF_USER_ID = 'USR-001';

function find(id: string): User {
  return users.find((u) => u.id === id) ?? notFound('El usuario');
}

function assertUniqueEmail(email: string, exceptId?: string): void {
  if (users.some((u) => u.id !== exceptId && u.email.toLowerCase() === email.trim().toLowerCase())) {
    throw new ServiceError('CONFLICT', 'Ya existe un usuario con este email.', { field: 'email' });
  }
}

/** Contraseña temporal de 16 caracteres (sin caracteres ambiguos). */
export function generateTemporaryPassword(): string {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!#$%&*?';
  const values = new Uint32Array(16);
  crypto.getRandomValues(values);
  return Array.from(values, (v) => alphabet[v % alphabet.length]).join('');
}

export const usersService = {
  list(): Promise<User[]> {
    return mockList(() => users);
  },

  get(id: string): Promise<User> {
    return mockRead(() => find(id));
  },

  /** Alta con contraseña temporal (estado "Pendiente" hasta el primer acceso). */
  create(input: CreateUserInput): Promise<User> {
    return mockWrite('users.create', () => {
      assertUniqueEmail(input.email);
      const user: User = {
        id: nextId(
          'USR-',
          users.map((u) => u.id),
        ),
        nombre: input.nombre.trim(),
        email: input.email.trim().toLowerCase(),
        rol: input.rol,
        estado: 'Pendiente',
        ultimoAcceso: null,
        creadoEl: new Date().toISOString().slice(0, 10),
        creadoPor: 'Admin SCIH',
        mfaActivo: false,
      };
      users.unshift(user);
      rolesService._syncCounts();
      record({
        accion: 'Crear',
        recurso: `Usuario ${user.id}`,
        descripcion: `Alta de ${user.nombre} con rol ${user.rol}.`,
        cambios: [{ campo: 'Rol', antes: '—', despues: user.rol }],
      });
      return user;
    });
  },

  update(id: string, input: UpdateUserInput): Promise<User> {
    return mockWrite('users.update', () => {
      const user = find(id);
      assertUniqueEmail(input.email, id);
      const cambios = [
        user.nombre !== input.nombre.trim() && { campo: 'Nombre', antes: user.nombre, despues: input.nombre.trim() },
        user.email !== input.email.trim().toLowerCase() && { campo: 'Email', antes: user.email, despues: input.email.trim().toLowerCase() },
        user.rol !== input.rol && { campo: 'Rol', antes: user.rol, despues: input.rol },
      ].filter(Boolean) as AuditEvent['cambios'];
      Object.assign(user, { nombre: input.nombre.trim(), email: input.email.trim().toLowerCase(), rol: input.rol });
      rolesService._syncCounts();
      record({ accion: 'Actualizar', recurso: `Usuario ${id}`, descripcion: 'Datos del usuario actualizados.', cambios });
      return user;
    });
  },

  /** Suspender (cierra sus sesiones) o reactivar. */
  setStatus(id: string, estado: Extract<UserStatus, 'Activo' | 'Suspendido'>): Promise<User> {
    return mockWrite('users.status', () => {
      if (id === SELF_USER_ID) throw new ServiceError('FORBIDDEN', 'No puedes suspender tu propia cuenta.');
      const user = find(id);
      const antes = user.estado;
      user.estado = estado;
      if (estado === 'Suspendido') sessions = sessions.filter((s) => s.userId !== id);
      record({
        accion: estado === 'Suspendido' ? 'Revocar' : 'Actualizar',
        recurso: `Usuario ${id}`,
        descripcion: estado === 'Suspendido' ? 'Cuenta suspendida y sesiones cerradas.' : 'Cuenta reactivada.',
        cambios: [{ campo: 'Estado', antes, despues: estado }],
      });
      return user;
    });
  },

  /** Eliminación (baja lógica en backend). */
  remove(id: string): Promise<void> {
    return mockWrite('users.remove', () => {
      if (id === SELF_USER_ID) throw new ServiceError('FORBIDDEN', 'No puedes eliminar tu propia cuenta.');
      const user = find(id);
      users.splice(users.indexOf(user), 1);
      sessions = sessions.filter((s) => s.userId !== id);
      rolesService._syncCounts();
      record({ accion: 'Eliminar', recurso: `Usuario ${id}`, descripcion: `Cuenta de ${user.nombre} eliminada.` });
    });
  },

  listSessions(userId: string): Promise<UserSession[]> {
    return mockList(() => sessions.filter((s) => s.userId === userId));
  },

  revokeSession(userId: string, sessionId: string): Promise<void> {
    return mockWrite('users.revokeSession', () => {
      const s = sessions.find((x) => x.id === sessionId && x.userId === userId) ?? notFound('La sesión');
      sessions = sessions.filter((x) => x !== s);
      record({ accion: 'Revocar', recurso: `Sesión ${sessionId}`, descripcion: `Sesión revocada en ${s.dispositivo}.` });
    });
  },

  revokeAllSessions(userId: string, forcePasswordChange: boolean): Promise<number> {
    return mockWrite('users.revokeAll', () => {
      const count = sessions.filter((s) => s.userId === userId).length;
      sessions = sessions.filter((s) => s.userId !== userId);
      if (forcePasswordChange) usersService._security(userId).restablecimientoPendiente = true;
      record({
        accion: 'Revocar',
        recurso: `Usuario ${userId}`,
        descripcion: `${count} sesiones revocadas${forcePasswordChange ? ' · cambio de contraseña obligatorio' : ''}.`,
      });
      return count;
    });
  },

  getSecurity(userId: string): Promise<UserSecuritySummary> {
    return mockRead(() => usersService._security(userId));
  },

  sendPasswordReset(userId: string): Promise<void> {
    return mockWrite('users.passwordReset', () => {
      const user = find(userId);
      usersService._security(userId).restablecimientoPendiente = true;
      record({ accion: 'Actualizar', recurso: `Usuario ${userId}`, descripcion: `Enlace de restablecimiento enviado a ${user.email}.` });
    });
  },

  resetMfa(userId: string): Promise<void> {
    return mockWrite('users.resetMfa', () => {
      const user = find(userId);
      user.mfaActivo = false;
      Object.assign(usersService._security(userId), { mfaActivo: false, codigosRecuperacion: 0, mfaDesde: undefined });
      record({
        accion: 'Revocar',
        recurso: `Usuario ${userId}`,
        descripcion: 'MFA restablecido: deberá configurarlo en su próximo acceso.',
      });
    });
  },

  /** Actividad reciente del usuario (desde Auditoría). */
  listActivity(userId: string): Promise<AuditEvent[]> {
    const user = users.find((u) => u.id === userId);
    return user ? auditService.listByUser(user.email, 6) : Promise.resolve([]);
  },

  _security(userId: string): UserSecuritySummary {
    if (!security.has(userId)) security.set(userId, securityFor(find(userId)));
    return security.get(userId)!;
  },
};
