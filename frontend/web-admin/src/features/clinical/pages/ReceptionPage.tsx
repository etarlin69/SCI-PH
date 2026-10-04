import React, { useEffect, useState } from 'react';
import type { Patient, AmbuTransfer } from '../types/clinical.types';
import { clinicalService } from '../services/clinicalService';

export const ReceptionPage: React.FC = () => {
  const [searchQuery, setSearchQuery] = useState('');
  const [patient, setPatient] = useState<Patient | null>(null);
  const [transfers, setTransfers] = useState<AmbuTransfer[]>([]);

  useEffect(() => {
    clinicalService.getAmbuTransfers().then(setTransfers);
  }, []);

  const handleSearch = async (e: React.FormEvent) => {
    e.preventDefault();
    const result = await clinicalService.searchPatient(searchQuery);
    setPatient(result);
  };

  const handleBiometricScan = () => {
    alert('Iniciando escáner biométrico/facial...');
    setPatient({
      id: 'P-101',
      nationalId: '402-0000000-1',
      fullName: 'Juan Pérez (Identificado por Biometría)',
      age: 45,
      gender: 'M',
      allergies: ['Penicilina'],
      medicalHistory: ['Hipertensión Arterial']
    });
  };

  return (
    <div style={{ padding: '20px' }}>
      <h1>Recepción y Admisión Prehospitalaria</h1>

      {/* Sección Ambu-Transfers */}
      <section style={{ marginBottom: '30px', background: '#f8f9fa', padding: '15px', borderRadius: '8px' }}>
        <h2> Ambulancias Entrantes (Ambu-Transfers - Rol 6)</h2>
        {transfers.length === 0 ? (
          <p>No hay transferencias de ambulancias registradas en camino.</p>
        ) : (
          <div style={{ display: 'grid', gap: '10px' }}>
            {transfers.map((t) => (
              <div key={t.id} style={{ padding: '10px', background: '#fff', borderLeft: '5px solid #dc3545', boxShadow: '0 1px 3px rgba(0,0,0,0.1)' }}>
                <strong>Ambulancia: {t.ambulanceCode}</strong> - Paciente: {t.patientName} | Priority: <span style={{ color: 'red' }}>{t.priority.toUpperCase()}</span> | ETA: {t.etaMinutes} min
                <p style={{ margin: '5px 0 0 0', fontSize: '14px', color: '#666' }}>Resumen: {t.conditionSummary}</p>
              </div>
            ))}
          </div>
        )}
      </section>

      {/* Búsqueda y Búsqueda Biométrica */}
      <section>
        <h2>Búsqueda de Pacientes</h2>
        <div style={{ display: 'flex', gap: '10px', marginBottom: '20px' }}>
          <form onSubmit={handleSearch}>
            <input
              type="text"
              placeholder="Buscar por Cédula / Nombre..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              style={{ padding: '8px', width: '250px' }}
            />
            <button type="submit" style={{ padding: '8px 16px', marginLeft: '5px' }}>Buscar</button>
          </form>
          <button onClick={handleBiometricScan} style={{ padding: '8px 16px', backgroundColor: '#17a2b8', color: '#fff', border: 'none', borderRadius: '4px' }}>
             Búsqueda Biométrica / Facial
          </button>
        </div>

        {patient && (
          <div style={{ border: '1px solid #28a745', padding: '15px', borderRadius: '8px', background: '#e8f5e9' }}>
            <h3>Ficha de Admisión Confirmada</h3>
            <p><strong>Paciente:</strong> {patient.fullName}</p>
            <p><strong>Cédula:</strong> {patient.nationalId}</p>
            <p><strong>Edad:</strong> {patient.age} años</p>
            <p><strong>Alergias:</strong> {patient.allergies.join(', ') || 'Sin registros'}</p>
          </div>
        )}
      </section>
    </div>
  );
};