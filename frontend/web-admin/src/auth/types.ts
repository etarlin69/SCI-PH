// DTOs de /api/web/auth (SCI-PH.Web.BFF/Controllers/AuthController.cs, AccountController.cs, MfaController.cs y
// SCI-PH.Application/DTOs/Security). JSON en camelCase, fechas ISO-8601 UTC.

/** UserAccount: lo que devuelve GET /me y viaja dentro de WebSession. */
export interface WebUser {
  id: string;
  userName: string;
  email: string | null;
  roles: string[];
  permissions: string[];
}

/** WebSession. El refresh token NO viaja aquí: solo en la cookie HttpOnly. */
export interface WebSession {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  user: WebUser;
}

/** Prueba de contraseña válida; solo sirve en POST mfa/verify y caduca. No es un access token. */
export interface MfaChallenge {
  token: string;
  expiresAtUtc: string;
}

export interface WebLoginRequest {
  identifier: string;
  password: string;
}

/** Exactamente uno de `session` o `mfaChallenge` viene informado. */
export interface WebLoginResponse {
  mfaRequired: boolean;
  session: WebSession | null;
  mfaChallenge: MfaChallenge | null;
}

/** Se envía `code` (TOTP) o `recoveryCode`, nunca ambos. */
export type MfaVerifyRequest =
  | { challengeToken: string; code: string; recoveryCode?: never }
  | { challengeToken: string; recoveryCode: string; code?: never };

export type MfaProof = { code: string } | { recoveryCode: string };

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface ResetPasswordRequest {
  userId: string;
  token: string;
  newPassword: string;
}

export interface ConfirmEmailRequest {
  userId: string;
  token: string;
}

export interface ActivateAccountRequest {
  token: string;
  password: string;
}

export interface MfaSetup {
  sharedKey: string;
  authenticatorUri: string;
}

export interface RecoveryCodes {
  codes: string[];
}
