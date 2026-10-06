import type { Assignment, Departamento, License, Puesto, StaffAccount, StaffHistoryEntry, StaffMember, StaffStatus } from '../../types';
import { CLINICS_MOCK } from './mockClinics';
import { daysAgo, inDays } from './time';

export const PUESTOS: Puesto[] = [
  'Médico emergenciólogo',
  'Médico internista',
  'Pediatra',
  'Enfermería',
  'Paramédico',
  'Técnico en emergencias',
  'Coordinador de turno',
  'Operador de despacho',
  'Conductor de ambulancia',
];

export const DEPARTAMENTOS: Departamento[] = [
  'Emergencias',
  'Cuidados intensivos',
  'Pediatría',
  'Medicina interna',
  'Prehospitalario',
  'Despacho',
  'Administración',
];

/** Rol de cuenta sugerido por puesto (valores reales de SecurityRoles; '' = sin cuenta). */
export const ROL_SUGERIDO: Record<Puesto, string> = {
  'Médico emergenciólogo': 'Doctor',
  'Médico internista': 'Doctor',
  Pediatra: 'Doctor',
  Enfermería: 'Nurse',
  Paramédico: 'Paramedic',
  'Técnico en emergencias': 'Paramedic',
  'Coordinador de turno': '',
  'Operador de despacho': 'Receptionist',
  'Conductor de ambulancia': '',
};

export const TURNOS = ['Mañana (07–15)', 'Tarde (15–23)', 'Noche (23–07)', 'Guardia 24 h'];

export const LICENSE_TYPES = [
  'Exequátur',
  'Colegio Médico Dominicano',
  'ACLS · Soporte vital cardiovascular avanzado',
  'BLS · Soporte vital básico',
  'PHTLS · Trauma prehospitalario',
  'PALS · Soporte vital pediátrico',
];

type Row = [
  id: string,
  nombre: string,
  email: string,
  puesto: Puesto,
  clinicId: string,
  dep: Departamento,
  estado: StaffStatus,
  cuenta: StaffAccount,
];

const linked = (userId: string): StaffAccount => ({ estado: 'vinculada', userId });

const ROWS: Row[] = [
  ['PER-0101', 'Dr. Luis García', 'garcia@sci-ph.test', 'Médico emergenciólogo', 'CLI-001', 'Emergencias', 'Activo', linked('USR-002')],
  ['PER-0102', 'Dra. Sofía Vargas', 'vargas@sci-ph.test', 'Médico internista', 'CLI-001', 'Medicina interna', 'Activo', linked('USR-012')],
  ['PER-0103', 'Dr. Félix Medina', 'medina@sci-ph.test', 'Médico emergenciólogo', 'CLI-001', 'Emergencias', 'Activo', linked('USR-018')],
  ['PER-0104', 'Rosa Almonte', 'almonte@sci-ph.test', 'Enfermería', 'CLI-001', 'Cuidados intensivos', 'Activo', linked('USR-013')],
  ['PER-0105', 'Carlos Pérez', 'perez@sci-ph.test', 'Paramédico', 'CLI-001', 'Prehospitalario', 'Activo', linked('USR-003')],
  ['PER-0106', 'Julio Castillo', 'castillo@sci-ph.test', 'Operador de despacho', 'CLI-001', 'Despacho', 'Activo', linked('USR-014')],
  ['PER-0107', 'Héctor Guzmán', 'guzman@sci-ph.test', 'Operador de despacho', 'CLI-001', 'Despacho', 'Activo', linked('USR-020')],
  ['PER-0108', 'Dra. Ana Jiménez', 'jimenez@sci-ph.test', 'Médico emergenciólogo', 'CLI-002', 'Emergencias', 'Activo', linked('USR-005')],
  ['PER-0109', 'María Santos', 'maria@sci-ph.test', 'Coordinador de turno', 'CLI-002', 'Administración', 'Activo', linked('USR-004')],
  ['PER-0110', 'Pedro Rosario', 'rosario@sci-ph.test', 'Paramédico', 'CLI-002', 'Prehospitalario', 'Activo', linked('USR-006')],
  ['PER-0111', 'Patricia Núñez', 'nunez@sci-ph.test', 'Enfermería', 'CLI-002', 'Emergencias', 'Activo', linked('USR-015')],
  ['PER-0112', 'Dr. Ramón Ortiz', 'ortiz@sci-ph.test', 'Médico internista', 'CLI-002', 'Medicina interna', 'Inactivo', linked('USR-008')],
  ['PER-0113', 'José Tavárez', 'tavarez@sci-ph.test', 'Paramédico', 'CLI-004', 'Prehospitalario', 'Activo', linked('USR-009')],
  ['PER-0114', 'Andrés Polanco', 'polanco@sci-ph.test', 'Paramédico', 'CLI-003', 'Prehospitalario', 'De licencia', linked('USR-016')],
  ['PER-0115', 'Laura Méndez', 'mendez@sci-ph.test', 'Coordinador de turno', 'CLI-003', 'Administración', 'Inactivo', linked('USR-007')],
  ['PER-0116', 'Miguel Herrera', 'herrera@sci-ph.test', 'Paramédico', 'CLI-004', 'Prehospitalario', 'Inactivo', linked('USR-011')],
  [
    'PER-0117',
    'Yolanda Reyes',
    'reyes@sci-ph.test',
    'Enfermería',
    'CLI-001',
    'Cuidados intensivos',
    'Activo',
    { estado: 'pendiente', invitationId: 'INV-2048' },
  ],
  [
    'PER-0118',
    'Dra. Isabel Mora',
    'mora@sci-ph.test',
    'Pediatra',
    'CLI-005',
    'Pediatría',
    'Activo',
    { estado: 'pendiente', invitationId: 'INV-2046' },
  ],
  [
    'PER-0119',
    'Juan Matos',
    'jmatos@sci-ph.test',
    'Conductor de ambulancia',
    'CLI-001',
    'Prehospitalario',
    'Activo',
    { estado: 'sin_acceso' },
  ],
  [
    'PER-0120',
    'Luis Encarnación',
    'lencarnacion@sci-ph.test',
    'Conductor de ambulancia',
    'CLI-002',
    'Prehospitalario',
    'Activo',
    { estado: 'sin_acceso' },
  ],
];

export const STAFF_MOCK: StaffMember[] = ROWS.map(([id, nombre, email, puesto, clinicId, departamento, estado, cuenta], i) => ({
  id,
  nombre,
  email,
  telefono: `809-555-${String(142 + i * 7).padStart(4, '0')}`,
  puesto,
  clinicId,
  departamento,
  estado,
  cuenta,
  fechaIngreso: daysAgo(195 - i * 6),
}));

export const ASSIGNMENTS_MOCK: Assignment[] = [
  { id: 'ASG-1', staffId: 'PER-0101', lugar: 'Clínica Central', departamento: 'Emergencias', turno: 'Mañana (07–15)', desde: daysAgo(117) },
  { id: 'ASG-2', staffId: 'PER-0101', lugar: 'Unidad AMB-07', departamento: 'Prehospitalario', turno: 'Guardia 24 h', desde: daysAgo(56) },
  {
    id: 'ASG-3',
    staffId: 'PER-0101',
    lugar: 'Hospital del Norte',
    departamento: 'Emergencias',
    turno: 'Noche (23–07)',
    desde: daysAgo(195),
    hasta: daysAgo(118),
  },
  ...STAFF_MOCK.filter((s) => s.id !== 'PER-0101').map((s, i) => ({
    id: `ASG-${i + 10}`,
    staffId: s.id,
    lugar: CLINICS_MOCK.find((c) => c.id === s.clinicId)?.nombre ?? s.clinicId,
    departamento: s.departamento,
    turno: TURNOS[i % TURNOS.length],
    desde: s.fechaIngreso,
    hasta: s.estado === 'Inactivo' ? daysAgo(20) : undefined,
  })),
];

export const LICENSES_MOCK: License[] = [
  { id: 'LIC-1', staffId: 'PER-0101', tipo: 'Exequátur', numero: 'EXQ-12345-08' },
  { id: 'LIC-2', staffId: 'PER-0101', tipo: 'Colegio Médico Dominicano', numero: 'CMD-44102', vence: inDays(96) },
  { id: 'LIC-3', staffId: 'PER-0101', tipo: 'ACLS · Soporte vital cardiovascular avanzado', numero: 'AHA-2024-55310', vence: inDays(23) },
  { id: 'LIC-4', staffId: 'PER-0101', tipo: 'BLS · Soporte vital básico', numero: 'AHA-2024-55311', vence: daysAgo(24) },
  { id: 'LIC-5', staffId: 'PER-0101', tipo: 'PHTLS · Trauma prehospitalario', numero: 'NAEMT-88120', vence: inDays(231) },
  { id: 'LIC-6', staffId: 'PER-0105', tipo: 'PHTLS · Trauma prehospitalario', numero: 'NAEMT-90211', vence: inDays(140) },
  { id: 'LIC-7', staffId: 'PER-0105', tipo: 'BLS · Soporte vital básico', numero: 'AHA-2025-11872', vence: inDays(12) },
  { id: 'LIC-8', staffId: 'PER-0104', tipo: 'BLS · Soporte vital básico', numero: 'AHA-2025-10044', vence: inDays(300) },
  { id: 'LIC-9', staffId: 'PER-0118', tipo: 'PALS · Soporte vital pediátrico', numero: 'AHA-2025-20981', vence: inDays(410) },
];

export const STAFF_HISTORY_MOCK: StaffHistoryEntry[] = [
  {
    id: 'H-1',
    staffId: 'PER-0101',
    fecha: daysAgo(24),
    titulo: 'Certificación BLS vencida',
    detalle: 'Aviso automático del sistema',
    tone: 'danger',
  },
  {
    id: 'H-2',
    staffId: 'PER-0101',
    fecha: daysAgo(56),
    titulo: 'Asignado a Unidad AMB-07 · Guardia 24 h',
    detalle: 'por Admin SCIH',
    tone: 'info',
  },
  {
    id: 'H-3',
    staffId: 'PER-0101',
    fecha: daysAgo(117),
    titulo: 'Traslado de Hospital del Norte a Clínica Central',
    detalle: 'por Carmen Batista',
    tone: 'info',
  },
  { id: 'H-4', staffId: 'PER-0101', fecha: daysAgo(169), titulo: 'Licencia ACLS registrada', detalle: 'por Admin SCIH', tone: 'success' },
  {
    id: 'H-5',
    staffId: 'PER-0101',
    fecha: daysAgo(195),
    titulo: 'Cuenta de usuario USR-002 vinculada',
    detalle: 'Invitación aceptada',
    tone: 'success',
  },
  {
    id: 'H-6',
    staffId: 'PER-0101',
    fecha: daysAgo(195),
    titulo: 'Alta en Personal · Médico emergenciólogo',
    detalle: 'por Admin SCIH',
    tone: 'success',
  },
];
