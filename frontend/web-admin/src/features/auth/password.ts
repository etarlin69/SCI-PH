export interface PasswordRule {
  id: string;
  label: string;
  test: (password: string) => boolean;
}

export type PasswordStrength = 0 | 1 | 2 | 3 | 4;

/** Política de contraseñas: espejo de IdentityPolicy (10 caracteres, mayúscula, minúscula, dígito y símbolo). El backend la valida de nuevo. */
export const PASSWORD_RULES: PasswordRule[] = [
  { id: 'length', label: 'Mínimo 10 caracteres', test: (v) => v.length >= 10 },
  { id: 'upper', label: 'Una mayúscula', test: (v) => /[A-ZÁÉÍÓÚÑ]/.test(v) },
  { id: 'lower', label: 'Una minúscula', test: (v) => /[a-záéíóúñ]/.test(v) },
  { id: 'number', label: 'Un número', test: (v) => /\d/.test(v) },
  { id: 'symbol', label: 'Un símbolo (!@#$…)', test: (v) => /[^A-Za-z0-9ÁÉÍÓÚÑáéíóúñ]/.test(v) },
];

export const STRENGTH_LABELS: Record<PasswordStrength, string> = { 0: '', 1: 'Débil', 2: 'Media', 3: 'Fuerte', 4: 'Muy fuerte' };

/** 0 vacía · 1 débil (≤ 2 reglas) · 2 media (≤ 4) · 3 fuerte (todas) · 4 muy fuerte (todas y ≥ 14 caracteres). */
export function passwordStrength(password: string): PasswordStrength {
  if (!password) return 0;
  const passed = PASSWORD_RULES.filter((r) => r.test(password)).length;
  if (passed <= 2) return 1;
  if (passed <= 4) return 2;
  return password.length >= 14 ? 4 : 3;
}

export const meetsPasswordPolicy = (password: string) => PASSWORD_RULES.every((r) => r.test(password));

/** Formato de email laxo para validar en cliente (el backend valida de nuevo). */
export const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
