import React, { useState } from 'react';
import { clinicalService } from '../services/clinicalService';

export const PharmacyAndDischargePage: React.FC = () => {
  const [encounterId, setEncounterId] = useState('');
  
  // Prescripción
  const [medication, setMedication] = useState('');
  const [dosage, setDosage] = useState('');
  const [route, setRoute] = useState('Oral');
  const [frequency, setFrequency] = useState('');

  // Egreso
  const [summary, setSummary] = useState('');

  const handleAddPrescription = async (e: React.FormEvent) => {
    e.preventDefault();
    await clinicalService.addPrescription(encounterId, {
      medication,
      dosage,
      route,
      frequency
    });
    alert('Orden médica de medicamento creada');
    setMedication(''); setDosage(''); setFrequency('');
  };

  const handleDischarge = async (e: React.FormEvent) => {
    e.preventDefault();
    const success = await clinicalService.dischargePatient(encounterId, summary);
    if (success) {
      alert('Resumen de Alta y Egreso completados exitosamente');
      setEncounterId('');
      setSummary('');
    }
  };

  return (
    <div style={{ padding: '20px' }}>
      <h1> Prescripción, Kardex (MAR) y Egreso Hospitalario</h1>

      <div style={{ marginBottom: '20px' }}>
        <label>
          <strong>ID del Encuentro Clínico Activo: </strong>
          <input
            type="text"
            value={encounterId}
            onChange={(e) => setEncounterId(e.target.value)}
            placeholder="Ingrese ID del encuentro..."
            style={{ padding: '6px', marginLeft: '10px' }}
          />
        </label>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '20px' }}>
        {/* Módulo de Prescripción de Medicamentos */}
        <div style={{ border: '1px solid #ccc', padding: '15px', borderRadius: '8px' }}>
          <h3> Emitir Órdenes Médicas (Recetas)</h3>
          <form onSubmit={handleAddPrescription} style={{ display: 'grid', gap: '10px' }}>
            <input type="text" placeholder="Medicamento (Buscador Catálogo)" value={medication} onChange={(e) => setMedication(e.target.value)} required />
            <input type="text" placeholder="Dosis (Ej. 500mg)" value={dosage} onChange={(e) => setDosage(e.target.value)} required />
            <select value={route} onChange={(e) => setRoute(e.target.value)}>
              <option value="Oral">Oral</option>
              <option value="IV">Intravenosa (IV)</option>
              <option value="IM">Intramuscular (IM)</option>
            </select>
            <input type="text" placeholder="Frecuencia (Ej. Cada 8 horas)" value={frequency} onChange={(e) => setFrequency(e.target.value)} required />
            <button type="submit" style={{ padding: '8px', backgroundColor: '#007bff', color: '#fff', border: 'none' }}>Guardar Orden</button>
          </form>
        </div>

        {/* Cierre de Atención y Alta */}
        <div style={{ border: '1px solid #ccc', padding: '15px', borderRadius: '8px' }}>
          <h3> Egreso Hospitalario / Resumen de Alta</h3>
          <form onSubmit={handleDischarge} style={{ display: 'grid', gap: '10px' }}>
            <textarea
              placeholder="Indicaciones finales y resumen clínico de alta..."
              value={summary}
              onChange={(e) => setSummary(e.target.value)}
              rows={5}
              required
            />
            <button type="submit" style={{ padding: '8px', backgroundColor: '#dc3545', color: '#fff', border: 'none' }}>
              Procesar Alta Definitiva
            </button>
          </form>
        </div>
      </div>
    </div>
  );
};