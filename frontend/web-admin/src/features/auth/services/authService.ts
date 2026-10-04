import {
  DEMO_ACCOUNTS,
  DEMO_RECOVERY_CODE,
  DEMO_TOTP,
  LOCK_MINUTES,
  MAX_LOGIN_ATTEMPTS,
  MAX_MFA_ATTEMPTS,
  RECENT_PASSWORDS,
  RESET_TOKENS,
  ACTIVATION_TOKENS,
  EMAIL_CONFIRM_TOKENS,
  ROLES_MOCK,
  USERS_MOCK,
} from '../../../data/mock';
import type {
  ActivationInfo,
  EmailConfirmStatus,
  LoginInput,
  MfaChallenge,
  MfaInput,
  Profile,
  ProfileInput,
  ResetTokenStatus,
  Session,
} from '../../../types';
import { setActor } from '../../../services/core/actor';
import { ServiceError } from '../../../services/core/errors';
import { mockRead, mockWrite } from '../../../services/core/mock';
import { record } from '../../audit/services/auditService';

const SESSION_KEY = 'sciph.mock.session';

let loginAttempts = 0;
let lockedUntil = 0;
const challenges = new Map<string, { userId: string; attempts: number }>();

function sessionFor(userId: string): Session {
  const user = USERS_MOCK.find((u) => u.id === userId);
  const role = ROLES_MOCK.find((r) => r.nombre === user?.rol);
  if (!user || !role) throw new ServiceError('SERVER', 'No se pudo cargar el perfil del usuario.');
  return {
    user: { id: user.id, nombre: user.nombre, email: user.email, rol: user.rol, permisos: role.permisos },
    expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
  };
}

function lockError(until: number): ServiceError {
  return new ServiceError('ACCOUNT_LOCKED', 'Cuenta bloqueada temporalmente por demasiados intentos.', { lockedUntil: until });
}

function maskEmail(email: string): string {
  const [name, domain] = email.split('@');
  return `${name.charAt(0)}${'*'.repeat(Math.max(2, name.length - 1))}@${domain}`;
}

export const authService = {
  getSession(): Session | null {
    try {
      const raw = sessionStorage.getItem(SESSION_KEY);
      if (!raw) return null;
      const session = JSON.parse(raw) as Session;
      setActor({ nombre: session.user.nombre, email: session.user.email, rol: session.user.rol });
      return session;
    } catch {
      return null;
    }
  },

  /** Paso 1: credenciales. */
  async login(input: LoginInput): Promise<MfaChallenge> {
    return mockWrite('auth.login', () => {
      if (Date.now() < lockedUntil) throw lockError(lockedUntil);
      const account = DEMO_ACCOUNTS.find(
        (a) => a.usuario.toLowerCase() === input.usuario.trim().toLowerCase() && a.password === input.password,
      );
      if (!account) {
        loginAttempts += 1;
        if (loginAttempts >= MAX_LOGIN_ATTEMPTS) {
          lockedUntil = Date.now() + LOCK_MINUTES * 60_000;
          loginAttempts = 0;
          throw lockError(lockedUntil);
        }
        throw new ServiceError('INVALID_CREDENTIALS', 'Email o contraseña incorrectos.', {
          attemptsLeft: MAX_LOGIN_ATTEMPTS - loginAttempts,
        });
      }
      loginAttempts = 0;
      const challengeId = `CH-${Date.now()}`;
      challenges.set(challengeId, { userId: account.userId, attempts: 0 });
      return { challengeId, attemptsLeft: MAX_MFA_ATTEMPTS, maskedEmail: maskEmail(account.usuario) };
    });
  },

  /** Paso 2: código TOTP de 6 dígitos o código de recuperación `XXXX-XXXX`. */
  async verifyMfa(input: MfaInput): Promise<Session> {
    return mockWrite('auth.mfa', () => {
      const ch = challenges.get(input.challengeId);
      if (!ch) throw new ServiceError('TOKEN_EXPIRED', 'La verificación caducó. Vuelve a iniciar sesión.');
      if (Date.now() < lockedUntil) throw lockError(lockedUntil);
      const ok = input.method === 'totp' ? input.code === DEMO_TOTP : input.code.toUpperCase() === DEMO_RECOVERY_CODE;
      if (!ok) {
        ch.attempts += 1;
        if (ch.attempts >= MAX_MFA_ATTEMPTS) {
          lockedUntil = Date.now() + LOCK_MINUTES * 60_000;
          challenges.delete(input.challengeId);
          throw lockError(lockedUntil);
        }
        throw new ServiceError('INVALID_CODE', input.method === 'totp' ? 'Código incorrecto.' : 'Código de recuperación no válido.', {
          attemptsLeft: MAX_MFA_ATTEMPTS - ch.attempts,
        });
      }
      challenges.delete(input.challengeId);
      const session = sessionFor(ch.userId);
      sessionStorage.setItem(SESSION_KEY, JSON.stringify(session));
      setActor({ nombre: session.user.nombre, email: session.user.email, rol: session.user.rol });
      record({
        accion: 'Inicio de sesión',
        recurso: `Sesión S-${Date.now().toString(16).slice(-6).toUpperCase()}`,
        descripcion: `Inicio de sesión con ${input.method === 'totp' ? 'código TOTP' : 'código de recuperación'}.`,
      });
      return session;
    });
  },

  async logout(): Promise<void> {
    record({ accion: 'Cierre de sesión', recurso: 'Sesión actual', descripcion: 'Cierre de sesión manual.' });
    sessionStorage.removeItem(SESSION_KEY);
  },

  /** Envía el enlace de recuperación. */
  async requestPasswordReset(email: string): Promise<void> {
    return mockWrite('auth.forgot', () => {
      if (email.trim().toLowerCase().startsWith('limite')) {
        throw new ServiceError('RATE_LIMITED', 'Demasiadas solicitudes. Espera unos minutos antes de pedir otro enlace.');
      }
    });
  },

  validateResetToken(token: string): Promise<ResetTokenStatus> {
    return mockRead(() => RESET_TOKENS[token] ?? 'expired');
  },

  /** Errores: TOKEN_EXPIRED, PASSWORD_REUSED. */
  async resetPassword(token: string, password: string): Promise<void> {
    return mockWrite('auth.reset', () => {
      if (RESET_TOKENS[token] !== 'valid') throw new ServiceError('TOKEN_EXPIRED', 'El enlace caducó o ya se usó.');
      if (RECENT_PASSWORDS.includes(password)) {
        throw new ServiceError('PASSWORD_REUSED', 'Debe ser distinta de tus últimas 5 contraseñas.');
      }
      RESET_TOKENS[token] = 'used';
    });
  },

  /** Comprueba el enlace de activación que llega en la invitación. */
  validateActivationToken(token: string): Promise<ActivationInfo> {
    return mockRead(() => {
      const t = ACTIVATION_TOKENS[token];
      if (!t) return { status: 'expired' as const };
      return t.status === 'valid' ? { status: t.status, email: t.email, nombre: t.nombre, rol: t.rol } : { status: t.status };
    });
  },

  /** Activa la cuenta fijando la primera contraseña. */
  async activateAccount(token: string, password: string): Promise<void> {
    return mockWrite('auth.activate', () => {
      const t = ACTIVATION_TOKENS[token];
      if (!t || t.status !== 'valid') throw new ServiceError('TOKEN_EXPIRED', 'El enlace de activación caducó o ya se usó.');
      if (password.length < 10) throw new ServiceError('VALIDATION', 'La contraseña no cumple la política.');
      t.status = 'used';
      record({ accion: 'Crear', recurso: `Cuenta ${t.email}`, descripcion: 'Cuenta activada desde el enlace de invitación.' });
    });
  },

  /** Confirma la dirección de email con el token del correo. */
  async confirmEmail(token: string): Promise<EmailConfirmStatus> {
    return mockRead(() => {
      const st = EMAIL_CONFIRM_TOKENS[token] ?? 'expired';
      // Idempotente: confirmar dos veces el mismo enlace válido devuelve "confirmed" (StrictMode repite efectos en desarrollo).
      return st === 'valid' ? ('confirmed' as const) : st;
    });
  },

  /** Reenvía el correo de confirmación. */
  async resendEmailConfirmation(email: string): Promise<void> {
    return mockWrite('auth.email.resend', () => {
      if (email.trim().toLowerCase().startsWith('limite')) {
        throw new ServiceError('RATE_LIMITED', 'Demasiadas solicitudes. Espera unos minutos antes de pedir otro enlace.');
      }
    });
  },

  getProfile(userId: string): Promise<Profile> {
    return mockRead(() => {
      const u = USERS_MOCK.find((x) => x.id === userId);
      if (!u) throw new ServiceError('NOT_FOUND', 'No se encontró tu perfil.');
      const extra = profileExtras.get(userId) ?? { telefono: '809-555-0100', notificacionesEmail: true, resumenSemanal: false };
      return {
        id: u.id,
        nombre: u.nombre,
        email: u.email,
        rol: u.rol,
        mfaActivo: u.mfaActivo,
        ultimoAcceso: u.ultimoAcceso,
        creadoEl: u.creadoEl,
        ...extra,
      };
    });
  },

  async updateProfile(userId: string, input: ProfileInput): Promise<Profile> {
    await mockWrite('auth.profile.update', () => {
      const u = USERS_MOCK.find((x) => x.id === userId);
      if (!u) throw new ServiceError('NOT_FOUND', 'No se encontró tu perfil.');
      u.nombre = input.nombre.trim();
      profileExtras.set(userId, {
        telefono: input.telefono.trim(),
        notificacionesEmail: input.notificacionesEmail,
        resumenSemanal: input.resumenSemanal,
      });
      record({ accion: 'Actualizar', recurso: `Usuario ${u.id}`, descripcion: 'Actualización del perfil propio.' });
    });
    return authService.getProfile(userId);
  },

  /** Cambio de contraseña desde el perfil. */
  async changePassword(current: string, next: string): Promise<void> {
    return mockWrite('auth.password.change', () => {
      if (!DEMO_ACCOUNTS.some((a) => a.password === current))
        throw new ServiceError('INVALID_CREDENTIALS', 'La contraseña actual no es correcta.');
      if (RECENT_PASSWORDS.includes(next)) throw new ServiceError('PASSWORD_REUSED', 'Debe ser distinta de tus últimas 5 contraseñas.');
      record({ accion: 'Actualizar', recurso: 'Contraseña propia', descripcion: 'Cambio de contraseña desde el perfil.' });
    });
  },
};

const profileExtras = new Map<string, { telefono: string; notificacionesEmail: boolean; resumenSemanal: boolean }>();
