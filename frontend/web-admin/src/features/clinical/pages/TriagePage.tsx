import React, { useState } from 'react';
import type { VitalSigns, TriagePriority } from '../types/clinical.types';
import { clinicalService } from '../services/clinicalService';

export const TriagePage: React.FC = () => {
  const [patientId, setPatientId] = useState('');
  const [priority, setPriority] = useState<TriagePriority>('yellow');
  const [vitals, setVitals] = useState<VitalSigns>({
    bloodPressure: '120/80',
    heartRate: 75,
    oxygenSaturation: 98,
    temperature: 36.5,
  });

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const success = await clinicalService.saveTriage(patientId, vitals, priority);
    if (success) {
      alert('Triage guardado correctamente');
      setPatientId('');
    } else {
      alert('Error al guardar el triage');
    }
  };

  return (
    <div style={{ padding: '20px' }}>
      <h1>Evaluación de Triage</h1>
      <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '10px', maxWidth: '400px' }}>
        <label>
          ID del Paciente:
          <input
            type="text"
            value={patientId}
            onChange={(e) => setPatientId(e.target.value)}
            required
            style={{ width: '100%', padding: '8px' }}
          />
        </label>

        <label>
          Prioridad:
          <select value={priority} onChange={(e) => setPriority(e.target.value as TriagePriority)} style={{ width: '100%', padding: '8px' }}>
            <option value="red">Roja (Emergencia)</option>
            <option value="orange">Naranja (Urgencia Alta)</option>
            <option value="yellow">Amarilla (Urgencia Media)</option>
            <option value="green">Verde (Menor)</option>
            <option value="blue">Azul (No Urgente)</option>
          </select>
        </label>

        <label>
          Presión Arterial:
          <input
            type="text"
            value={vitals.bloodPressure}
            onChange={(e) => setVitals({ ...vitals, bloodPressure: e.target.value })}
            style={{ width: '100%', padding: '8px' }}
          />
        </label>

        <label>
          Frecuencia Cardíaca (BPM):
          <input
            type="number"
            value={vitals.heartRate}
            onChange={(e) => setVitals({ ...vitals, heartRate: Number(e.target.value) })}
            style={{ width: '100%', padding: '8px' }}
          />
        </label>

        <button type="submit" style={{ padding: '10px', marginTop: '10px' }}>Guardar Triage</button>
      </form>
    </div>
  );
};