import React, { useEffect, useState } from 'react';
import type { ClinicalEncounter, DiagnosticType } from '../types/clinical.types';
import { clinicalService } from '../services/clinicalService';

export const EncounterDashboardPage: React.FC = () => {
  const [encounters, setEncounters] = useState<ClinicalEncounter[]>([]);
  const [selectedEncounter, setSelectedEncounter] = useState<ClinicalEncounter | null>(null);

  // Form Diagnóstico
  const [diagCode, setDiagCode] = useState('');
  const [diagDesc, setDiagDesc] = useState('');
  const [diagType, setDiagType] = useState<DiagnosticType>('presumptive');

  // Form Nota SOAP
  const [soapS, setSoapS] = useState('');
  const [soapO, setSoapO] = useState('');
  const [soapA, setSoapA] = useState('');
  const [soapP, setSoapP] = useState('');

  useEffect(() => {
    clinicalService.getEncounters().then((data) => {
      setEncounters(data);
      if (data.length > 0) setSelectedEncounter(data[0]);
    });
  }, []);

  const handleAddDiagnostic = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedEncounter) return;
    await clinicalService.addDiagnostic(selectedEncounter.id, {
      code: diagCode,
      description: diagDesc,
      type: diagType,
    });
    alert('Diagnóstico agregado correctamente');
    setDiagCode('');
    setDiagDesc('');
  };

  const handleAddSoapNote = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedEncounter) return;
    await clinicalService.addNote(selectedEncounter.id, {
      author: 'Dr. Usuario Activo',
      subjective: soapS,
      objective: soapO,
      assessment: soapA,
      plan: soapP,
    });
    alert('Nota de evolución SOAP registrada');
    setSoapS(''); setSoapO(''); setSoapA(''); setSoapP('');
  };

  return (
    <div style={{ padding: '20px' }}>
      <h1>Dashboard de Atención y Expediente Clínico</h1>

      <div style={{ display: 'grid', gridTemplateColumns: '300px 1fr', gap: '20px' }}>
        {/* Kanban / Lista lateral de Pacientes Activos */}
        <div style={{ background: '#f4f4f4', padding: '15px', borderRadius: '8px' }}>
          <h3>Pacientes Ingresados</h3>
          {encounters.map((enc) => (
            <div
              key={enc.id}
              onClick={() => setSelectedEncounter(enc)}
              style={{
                padding: '10px',
                marginBottom: '10px',
                background: selectedEncounter?.id === enc.id ? '#007bff' : '#fff',
                color: selectedEncounter?.id === enc.id ? '#fff' : '#000',
                borderRadius: '4px',
                cursor: 'pointer'
              }}
            >
              <strong>{enc.patient.fullName}</strong>
              <div>Cama: {enc.assignedBed} | Prioridad: {enc.triagePriority}</div>
            </div>
          ))}
        </div>

        {/* Detalle del Expediente Clínico */}
        {selectedEncounter ? (
          <div style={{ border: '1px solid #ccc', padding: '20px', borderRadius: '8px' }}>
            <h2>Expediente Clínico Activo - {selectedEncounter.patient.fullName}</h2>
            <p><strong>Cédula:</strong> {selectedEncounter.patient.nationalId} | <strong>Edad:</strong> {selectedEncounter.patient.age} años</p>
            <p><strong>Alergias:</strong> {selectedEncounter.patient.allergies.join(', ')}</p>

            <hr />

            {/* Módulo de Diagnósticos */}
            <h3> Diagnósticos (CIE-10 / SNOMED)</h3>
            <form onSubmit={handleAddDiagnostic} style={{ display: 'flex', gap: '10px', marginBottom: '15px' }}>
              <input type="text" placeholder="Código (ej. J18.9)" value={diagCode} onChange={(e) => setDiagCode(e.target.value)} required style={{ padding: '5px' }} />
              <input type="text" placeholder="Descripción" value={diagDesc} onChange={(e) => setDiagDesc(e.target.value)} required style={{ width: '200px', padding: '5px' }} />
              <select value={diagType} onChange={(e) => setDiagType(e.target.value as DiagnosticType)}>
                <option value="presumptive">Presuntivo</option>
                <option value="definitive">Definitivo</option>
              </select>
              <button type="submit" style={{ padding: '5px 10px' }}>Agregar</button>
            </form>

            <hr />

            {/* Evolución y Nota SOAP */}
            <h3> Evolución Clínica (Nota SOAP)</h3>
            <form onSubmit={handleAddSoapNote} style={{ display: 'grid', gap: '10px', maxWidth: '500px' }}>
              <textarea placeholder="Subjetivo (S) - Síntomas reportados..." value={soapS} onChange={(e) => setSoapS(e.target.value)} required />
              <textarea placeholder="Objetivo (O) - Hallazgos de examen físico..." value={soapO} onChange={(e) => setSoapO(e.target.value)} required />
              <textarea placeholder="Apreciación (A) - Diagnóstico o análisis..." value={soapA} onChange={(e) => setSoapA(e.target.value)} required />
              <textarea placeholder="Plan (P) - Indicaciones y conducta..." value={soapP} onChange={(e) => setSoapP(e.target.value)} required />
              <button type="submit" style={{ padding: '8px', backgroundColor: '#28a745', color: '#fff', border: 'none', borderRadius: '4px' }}>
                Guardar Nota SOAP
              </button>
            </form>
          </div>
        ) : (
          <p>Seleccione un paciente de la lista para ver su expediente.</p>
        )}
      </div>
    </div>
  );
};