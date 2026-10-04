import type { DateTime } from '../../../types/common';
import type { ModuleKey, PermissionMatrix } from '../../roles/types/roles.types';

export interface SessionUser {
  id: string;
  nombre: string;
  email: string;
  rol: string;
  permisos: PermissionMatrix;
}

export interface Session {
  user: SessionUser;
  expiresAt: DateTime;
}

export interface LoginInput {
  usuario: string;
  password: string;
  recordarEquipo: boolean;
}

export interface MfaChallenge {
  challengeId: string;
  attemptsLeft: number;
  maskedEmail: string;
}

export type MfaMethod = 'totp' | 'recovery';

export interface MfaInput {
  challengeId: string;
  code: string;
  method: MfaMethod;
}

export type ResetTokenStatus = 'valid' | 'expired' | 'used';

export interface ActivationInfo {
  status: ResetTokenStatus;
  email?: string;
  nombre?: string;
  rol?: string;
}

export type EmailConfirmStatus = 'confirmed' | 'expired' | 'used';

export interface ProfileInput {
  nombre: string;
  telefono: string;
  notificacionesEmail: boolean;
  resumenSemanal: boolean;
}

export interface Profile extends ProfileInput {
  id: string;
  email: string;
  rol: string;
  mfaActivo: boolean;
  ultimoAcceso: string | null;
  creadoEl: string;
}

export interface PasswordRule {
  id: string;
  label: string;
  test: (password: string) => boolean;
}

export type PasswordStrength = 0 | 1 | 2 | 3 | 4;

export type RouteModule = ModuleKey;
