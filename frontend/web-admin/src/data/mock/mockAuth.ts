export interface DemoAccount {
  userId: string;
  usuario: string;
  password: string;
}

export const DEMO_ACCOUNTS: DemoAccount[] = [
  { userId: 'USR-001', usuario: 'admin@scih.com', password: 'Clave2026!' },
  { userId: 'USR-017', usuario: 'cruz@auditores.do', password: 'Clave2026!' },
];

export const DEMO_TOTP = '123456';
export const DEMO_RECOVERY_CODE = '7KQ2-M9XD';

export const RECENT_PASSWORDS = ['Clave2026!'];

export const RESET_TOKENS: Record<string, 'valid' | 'expired' | 'used'> = {
  demo: 'valid',
  caducado: 'expired',
  usado: 'used',
};

export const ACTIVATION_TOKENS: Record<string, { status: 'valid' | 'expired' | 'used'; email: string; nombre: string; rol: string }> = {
  demo: { status: 'valid', email: 'nuevo.usuario@scih.com', nombre: 'Nuevo Usuario', rol: 'Médico' },
  caducado: { status: 'expired', email: 'caducado@scih.com', nombre: 'Usuario Caducado', rol: 'Enfermería' },
  usado: { status: 'used', email: 'activo@scih.com', nombre: 'Usuario Activo', rol: 'Recepción' },
};

export const EMAIL_CONFIRM_TOKENS: Record<string, 'valid' | 'expired' | 'used'> = {
  demo: 'valid',
  caducado: 'expired',
  usado: 'used',
};

export const MAX_LOGIN_ATTEMPTS = 5;
export const MAX_MFA_ATTEMPTS = 5;
export const LOCK_MINUTES = 15;
