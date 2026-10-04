import React, { useState } from 'react';
import { LoginForm } from '../components/LoginForm';
import { MfaChallengeModal } from '../components/MfaChallengeModal';
import { ContextSelector } from '../components/ContextSelector';
import { authService } from '../services/authService';
import type { LoginRequest, ClinicContext } from '../types/auth.types';

type Step = 'LOGIN' | 'MFA' | 'SELECT_CLINIC';

export const LoginPage: React.FC = () => {
  const [step, setStep] = useState<Step>('LOGIN');
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [mfaToken, setMfaToken] = useState<string>('');
  const [tempAccessToken, setTempAccessToken] = useState<string>('');
  const [availableClinics, setAvailableClinics] = useState<ClinicContext[]>([]);

  const handleLogin = async (credentials: LoginRequest) => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await authService.login(credentials);
      if (!response.isSuccess) {
        setError(response.message || 'Credenciales inválidas');
        return;
      }

      if (response.requiresMfa && response.mfaChallengeToken) {
        setMfaToken(response.mfaChallengeToken);
        setStep('MFA');
      } else if (response.availableClinics && response.availableClinics.length > 0) {
        setTempAccessToken(response.accessToken || '');
        setAvailableClinics(response.availableClinics);
        setStep('SELECT_CLINIC');
      } else {
        localStorage.setItem('token', response.accessToken || '');
        window.location.href = '/dashboard';
      }
    } catch {
      setError('Ocurrió un error al conectar con el servidor.');
    } finally {
      setIsLoading(false);
    }
  };

  const handleVerifyMfa = async (code: string) => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await authService.verifyMfa({ mfaChallengeToken: mfaToken, code });
      if (!response.isSuccess) {
        setError(response.message || 'Código MFA inválido');
        return;
      }

      if (response.availableClinics && response.availableClinics.length > 0) {
        setTempAccessToken(response.accessToken || '');
        setAvailableClinics(response.availableClinics);
        setStep('SELECT_CLINIC');
      } else {
        localStorage.setItem('token', response.accessToken || '');
        window.location.href = '/dashboard';
      }
    } catch {
      setError('Error al verificar el código MFA.');
    } finally {
      setIsLoading(false);
    }
  };

  const handleSelectContext = async (clinicId: string) => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await authService.selectContext({ clinicId }, tempAccessToken);
      if (response.isSuccess && response.accessToken) {
        localStorage.setItem('token', response.accessToken);
        window.location.href = '/dashboard';
      } else {
        setError(response.message || 'No se pudo seleccionar la sede.');
      }
    } catch {
      setError('Error al guardar la sede seleccionada.');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="auth-layout">
      <div className="auth-card">
        {step === 'LOGIN' && (
          <LoginForm onSubmit={handleLogin} isLoading={isLoading} error={error} />
        )}

        {step === 'SELECT_CLINIC' && (
          <ContextSelector
            clinics={availableClinics}
            onSelect={handleSelectContext}
            isLoading={isLoading}
          />
        )}

        {step === 'MFA' && (
          <MfaChallengeModal
            onVerify={handleVerifyMfa}
            isLoading={isLoading}
            error={error}
            onCancel={() => {
              setStep('LOGIN');
              setError(null);
            }}
          />
        )}
      </div>
    </div>
  );
};