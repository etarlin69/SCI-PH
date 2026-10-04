import type { Ambulance, Diagnosis, Medication, Procedure, Shift } from '../../types';

export const MEDICATIONS_MOCK: Medication[] = [
  ['MED-001', 'Acetaminofén', '500 mg', 'Tableta', 'mg', 'Activo', 214],
  ['MED-002', 'Ibuprofeno', '400 mg', 'Tableta', 'mg', 'Activo', 96],
  ['MED-003', 'Adrenalina (epinefrina)', '1 mg/mL', 'Inyectable', 'mL', 'Activo', 58],
  ['MED-004', 'Oxígeno medicinal', '99 %', 'Gas medicinal', 'L/min', 'Activo', 402],
  ['MED-005', 'Salbutamol', '100 mcg/dosis', 'Inhalador', 'dosis', 'Activo', 77],
  ['MED-006', 'Nitroglicerina', '0.4 mg', 'Tableta sublingual', 'mg', 'Activo', 31],
  ['MED-007', 'Ácido acetilsalicílico', '100 mg', 'Tableta', 'mg', 'Activo', 45],
  ['MED-008', 'Naloxona', '0.4 mg/mL', 'Inyectable', 'mL', 'Activo', 7],
  ['MED-009', 'Ketorolaco', '30 mg/mL', 'Inyectable', 'mL', 'Activo', 0],
  ['MED-010', 'Metoclopramida', '10 mg/2 mL', 'Inyectable', 'mL', 'Inactivo', 0],
].map(([codigo, nombre, concentracion, presentacion, unidad, estado, uso], i) => ({
  id: `med-${i + 1}`,
  codigo: codigo as string,
  nombre: nombre as string,
  concentracion: concentracion as string,
  presentacion: presentacion as string,
  unidad: unidad as string,
  estado: estado as Medication['estado'],
  uso: uso as number,
}));

export const DIAGNOSES_MOCK: Diagnosis[] = [
  ['I21.9', 'Infarto agudo de miocardio, sin otra especificación', 'Cardiovascular', 'Activo', 38],
  ['I46.9', 'Paro cardíaco, no especificado', 'Cardiovascular', 'Activo', 11],
  ['I63.9', 'Infarto cerebral, no especificado', 'Neurológico', 'Activo', 17],
  ['R56.8', 'Otras convulsiones y las no especificadas', 'Neurológico', 'Activo', 21],
  ['S06.0', 'Conmoción cerebral', 'Trauma', 'Activo', 42],
  ['T07', 'Traumatismos múltiples, no especificados', 'Trauma', 'Activo', 27],
  ['J45.9', 'Asma, no especificada', 'Respiratorio', 'Activo', 33],
  ['J96.0', 'Insuficiencia respiratoria aguda', 'Respiratorio', 'Activo', 14],
  ['R55', 'Síncope y colapso', 'Otros', 'Activo', 30],
  ['T78.2', 'Choque anafiláctico, no especificado', 'Otros', 'Activo', 0],
].map(([codigo, descripcion, categoria, estado, uso], i) => ({
  id: `dx-${i + 1}`,
  codigo: codigo as string,
  descripcion: descripcion as string,
  categoria: categoria as string,
  estado: estado as Diagnosis['estado'],
  uso: uso as number,
}));

export const PROCEDURES_MOCK: Procedure[] = [
  ['PRC-001', 'Reanimación cardiopulmonar avanzada', 'Emergencias', 'Soporte vital avanzado', 14],
  ['PRC-002', 'Intubación endotraqueal', 'Emergencias', 'Vía aérea avanzada', 9],
  ['PRC-003', 'Inmovilización espinal', 'Trauma', 'Trauma prehospitalario', 36],
  ['PRC-004', 'Control de hemorragia con torniquete', 'Trauma', 'Control de hemorragias', 18],
  ['PRC-005', 'Electrocardiograma de 12 derivaciones', 'Cardiología', 'Código infarto', 88],
  ['PRC-006', 'Desfibrilación', 'Cardiología', 'Soporte vital avanzado', 12],
  ['PRC-007', 'Acceso venoso periférico', 'Enfermería', 'Estándar', 412],
  ['PRC-008', 'Glucometría capilar', 'Enfermería', 'Estándar', 205],
  ['PRC-009', 'Oxigenoterapia', 'Respiratorio', 'Estándar', 290],
  ['PRC-010', 'Nebulización', 'Respiratorio', 'Estándar', 0],
].map(([codigo, descripcion, especialidad, protocolo, uso], i) => ({
  id: `prc-${i + 1}`,
  codigo: codigo as string,
  descripcion: descripcion as string,
  especialidad: especialidad as string,
  protocolo: protocolo as string,
  estado: 'Activo' as const,
  uso: uso as number,
}));

export const AMBULANCES_MOCK: Ambulance[] = [
  ['AMB-02', 'Avanzada', 'Base Regional Este', 'J. Tavárez, L. Núñez', 'Disponible', 2],
  ['AMB-03', 'Intermedia', 'Av. Kennedy esq. Lincoln', 'P. Rosario', 'En uso', 2],
  ['AMB-05', 'Intermedia', 'Base Norte', 'A. Valdez', 'Disponible', 1],
  ['AMB-07', 'Avanzada', 'En ruta → Clínica Central · 4 min', 'C. Pérez, R. Peña', 'En uso', 3],
  ['AMB-09', 'Avanzada', 'Base Norte', 'Sin asignar', 'Disponible', 0],
  ['AMB-12', 'Básica', 'Base Central', 'J. Matos', 'Disponible', 1],
  ['AMB-15', 'Básica', 'Taller de flota', 'Sin asignar', 'Mantenimiento', 0],
  ['AMB-18', 'Intermedia', 'Base Sur', 'F. Lora', 'Disponible', 0],
  ['AMB-20', 'Avanzada', 'Autopista Duarte km 9', 'Ó. Montero, S. Brito', 'En uso', 2],
  ['AMB-22', 'Básica', 'Base Central', 'L. Encarnación', 'Disponible', 1],
].map(([codigo, tipo, ubicacion, personal, estado, uso], i) => ({
  id: `amb-${i + 1}`,
  codigo: codigo as string,
  tipo: tipo as Ambulance['tipo'],
  ubicacion: ubicacion as string,
  personal: personal as string,
  estado: estado as Ambulance['estado'],
  uso: uso as number,
}));

export const SHIFTS_MOCK: Shift[] = [
  ['Mañana', '07:00 – 15:00', 'Emergencias · Clínica Central', 'Activo', 14],
  ['Tarde', '15:00 – 23:00', 'Emergencias · Clínica Central', 'Activo', 12],
  ['Noche', '23:00 – 07:00', 'Emergencias · Clínica Central', 'Activo', 8],
  ['Guardia 24 h', '07:00 – 07:00', 'Unidades prehospitalarias', 'Activo', 6],
  ['Fin de semana', '07:00 – 19:00', 'Coordinación de traslados', 'Activo', 4],
  ['Madrugada', '01:00 – 07:00', 'Central de despacho', 'Activo', 3],
  ['Guardia UCI', '19:00 – 07:00', 'Cuidados intensivos · Clínica Central', 'Activo', 5],
  ['Coordinación noche', '23:00 – 07:00', 'Coordinación de traslados', 'Activo', 2],
  ['Refuerzo pediatría', '12:00 – 20:00', 'Pediatría · Hospital Infantil Sur', 'Inactivo', 0],
  ['Refuerzo temporada', '10:00 – 18:00', 'Unidades prehospitalarias', 'Inactivo', 0],
].map(([nombre, horario, area, estado, uso], i) => ({
  id: `tur-${i + 1}`,
  nombre: nombre as string,
  horario: horario as string,
  area: area as string,
  estado: estado as Shift['estado'],
  uso: uso as number,
}));
