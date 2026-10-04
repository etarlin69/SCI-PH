import React, { useState } from 'react';
import type { LoginRequest } from '../types/auth.types';

interface LoginFormProps {
  onSubmit: (credentials: LoginRequest) => Promise<void>;
  isLoading: boolean;
  error?: string | null;
}

export const LoginForm: React.FC<LoginFormProps> = ({ onSubmit, isLoading, error }) => {
  const [userNameOrEmail, setUserNameOrEmail] = useState('');
  const [password, setPassword] = useState('');

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!userNameOrEmail || !password) return;
    onSubmit({ userNameOrEmail, password });
  };

  return (
    <form onSubmit={handleSubmit} className="auth-form">
      <h2>Iniciar Sesión</h2>
      <p className="auth-subtitle">Ingresa tus credenciales para acceder al sistema clínico</p>

      {error && <div className="auth-error-alert">{error}</div>}

      <div className="form-group">
        <label htmlFor="email">Usuario o Correo Electrónico</label>
        <input
          id="email"
          type="text"
          value={userNameOrEmail}
          onChange={(e) => setUserNameOrEmail(e.target.value)}
          placeholder="ejemplo@clinica.com"
          required
          disabled={isLoading}
        />
      </div>

      <div className="form-group">
        <label htmlFor="password">Contraseña</label>
        <input
          id="password"
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          placeholder="••••••••"
          required
          disabled={isLoading}
        />
      </div>

      <button type="submit" className="btn-primary" disabled={isLoading}>
        {isLoading ? 'Autenticando...' : 'Ingresar'}
      </button>
    </form>
  );
};