export interface LoginRequest {
  userNameOrEmail: string;
  password: string;
}

export interface ClinicContext {
  id: string;
  name: string;
  code?: string;
}

export interface LoginResponse {
  isSuccess: boolean;
  message?: string;
  accessToken?: string;
  requiresMfa?: boolean;
  mfaChallengeToken?: string;
  availableClinics?: ClinicContext[];
}

export interface VerifyMfaRequest {
  mfaChallengeToken: string;
  code: string;
}

export interface SelectContextRequest {
  clinicId: string;
}

export interface SelectContextResponse {
  isSuccess: boolean;
  message?: string;
  accessToken?: string;
}