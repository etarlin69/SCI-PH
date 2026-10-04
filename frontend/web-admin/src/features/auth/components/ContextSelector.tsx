import React from 'react';
import type { ClinicContext } from '../types/auth.types';

interface ContextSelectorProps {
  clinics: ClinicContext[];
  onSelect: (clinicId: string) => Promise<void>;
  isLoading: boolean;
}

export const ContextSelector: React.FC<ContextSelectorProps> = ({
  clinics,
  onSelect,
  isLoading,
}) => {
  return (
    <div className="context-selector">
      <h2>Selecciona una sede</h2>
      <p className="auth-subtitle">Elige la clínica a la que deseas ingresar</p>

      <div className="clinic-list" style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem', margin: '1.5rem 0' }}>
        {clinics.map((clinic) => (
          <button
            key={clinic.id}
            type="button"
            className="btn-secondary"
            onClick={() => onSelect(clinic.id)}
            disabled={isLoading}
          >
            {clinic.name}
          </button>
        ))}
      </div>
    </div>
  );
};