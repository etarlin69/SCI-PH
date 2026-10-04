import React, { useState } from 'react';

interface MfaChallengeModalProps {
  onVerify: (code: string) => Promise<void>;
  isLoading: boolean;
  error?: string | null;
  onCancel: () => void;
}

export const MfaChallengeModal: React.FC<MfaChallengeModalProps> = ({
  onVerify,
  isLoading,
  error,
  onCancel,
}) => {
  const [code, setCode] = useState('');

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (code.trim()) {
      onVerify(code);
    }
  };

  return (
    <div className="mfa-modal">
      <h2>Autenticación de Dos Factores</h2>
      <p className="auth-subtitle">Ingresa el código enviado a tu dispositivo o aplicación</p>

      {error && <div className="auth-error-alert">{error}</div>}

      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label htmlFor="mfa-code">Código de verificación</label>
          <input
            id="mfa-code"
            type="text"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            placeholder="123456"
            required
            disabled={isLoading}
            autoFocus
          />
        </div>

        <div style={{ display: 'flex', gap: '0.5rem', marginTop: '1rem' }}>
          <button type="submit" className="btn-primary" disabled={isLoading || !code}>
            {isLoading ? 'Verificando...' : 'Verificar'}
          </button>
          <button type="button" className="btn-secondary" onClick={onCancel} disabled={isLoading}>
            Cancelar
          </button>
        </div>
      </form>
    </div>
  );
};