import type { DateTime, ISODate } from '../../../types/common';

export type UserStatus = 'Activo' | 'Pendiente' | 'Suspendido' | 'Inactivo';

export interface User {
  id: string;
  nombre: string;
  email: string;
  rol: string;
  estado: UserStatus;
  /** `null` si nunca ha iniciado sesión. */
  ultimoAcceso: DateTime | null;
  creadoEl: ISODate;
  creadoPor: string;
  mfaActivo: boolean;
  staffId?: string;
}

export type SessionKind = 'web' | 'mobile' | 'device';

export interface UserSession {
  id: string;
  userId: string;
  dispositivo: string;
  tipo: SessionKind;
  plataforma: string;
  ip: string;
  ubicacion: string;
  nuevaUbicacion?: boolean;
  inicio: DateTime;
  ultimaActividad: DateTime;
}

export type PasswordDelivery = 'email' | 'manual';

export interface CreateUserInput {
  nombre: string;
  email: string;
  rol: string;
  entrega: PasswordDelivery;
  exigirMfa: boolean;
  temporaryPassword: string;
}

export interface UpdateUserInput {
  nombre: string;
  email: string;
  rol: string;
}

export interface UserFilters {
  q: string;
  rol: string;
  estado: UserStatus | '';
}

export interface UserSecuritySummary {
  mfaActivo: boolean;
  mfaDesde?: ISODate;
  codigosRecuperacion: number;
  passwordCambiadaEl: ISODate;
  passwordCaducaEnDias: number;
  restablecimientoPendiente: boolean;
  intentosFallidos7d: number;
  ultimoIntentoFallido?: { fecha: DateTime; ip: string };
}
