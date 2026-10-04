import type {
  LoginRequest,
  LoginResponse,
  VerifyMfaRequest,
  SelectContextRequest,
  SelectContextResponse,
} from '../types/auth.types';

export const authService = {
  async login(credentials: LoginRequest): Promise<LoginResponse> {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(credentials),
    });
    return response.json();
  },

  async verifyMfa(data: VerifyMfaRequest): Promise<LoginResponse> {
    const response = await fetch('/api/auth/verify-mfa', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data),
    });
    return response.json();
  },

  async selectContext(data: SelectContextRequest, token?: string): Promise<SelectContextResponse> {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
    };
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }

    const response = await fetch('/api/auth/select-context', {
      method: 'POST',
      headers,
      body: JSON.stringify(data),
    });
    return response.json();
  },
};