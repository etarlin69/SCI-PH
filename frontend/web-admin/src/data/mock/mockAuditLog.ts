import type { AuditAction, AuditChange, AuditEvent } from '../../types';
import { MOCK_NOW, seededRandom } from './time';

const USERS: [nombre: string, email: string, rol: string, ua: string, ip: string][] = [
  ['Admin SCIH', 'admin@scih.com', 'Administrador', 'Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) Safari/17.5', '10.10.1.2'],
  ['Carmen Batista', 'batista@scih.com', 'Administrador', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/129.0', '10.10.1.5'],
  ['Dr. Luis García', 'garcia@scih.com', 'Médico', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/129.0', '10.10.2.31'],
  ['Dra. Ana Jiménez', 'jimenez@scih.com', 'Médico', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/129.0', '10.10.2.44'],
  ['María Santos', 'maria@scih.com', 'Coordinador', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Edg/129.0', '10.10.3.8'],
  ['Carlos Pérez', 'perez@scih.com', 'Paramédico', 'SCI-PH Móvil 3.2.1 (Android 14; Samsung SM-X200)', '10.20.4.17'],
  ['José Tavárez', 'tavarez@scih.com', 'Paramédico', 'SCI-PH Móvil 3.2.1 (Android 14; Samsung SM-X200)', '10.20.4.22'],
  ['Rosa Almonte', 'almonte@scih.com', 'Enfermero', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/129.0', '10.10.5.12'],
  ['Julio Castillo', 'castillo@scih.com', 'Operador', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/129.0', '10.10.6.3'],
  ['Elena Cruz', 'cruz@auditores.do', 'Auditor externo', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Firefox/131.0', '186.7.40.12'],
  ['Dr. Ramón Ortiz', 'ortiz@scih.com', 'Médico', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Firefox/131.0', '186.7.33.140'],
];

export const AUDIT_USERS = USERS.map(([nombre]) => nombre);

export const AUDIT_ACTIONS: AuditAction[] = [
  'Inicio de sesión',
  'Cierre de sesión',
  'Consultar',
  'Crear',
  'Actualizar',
  'Eliminar',
  'Revocar',
  'Exportar',
];

interface Template {
  weight: number;
  accion: AuditAction;
  recurso: () => string;
  users: number[];
  errorRate?: number;
  errors?: string[];
  descripcion: string;
  cambios?: (recurso: string) => AuditChange[];
}

const rnd = seededRandom(20260926);
const pick = <T>(xs: T[]) => xs[Math.floor(rnd() * xs.length)];
const n2 = () => String(10 + Math.floor(rnd() * 89));
const hex = () =>
  Math.floor(rnd() * 0xffffff)
    .toString(16)
    .toUpperCase()
    .padStart(6, '0');

const ALL = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
const ADM = [0, 1];

const TEMPLATES: Template[] = [
  {
    weight: 22,
    accion: 'Inicio de sesión',
    recurso: () => `Sesión S-${hex()}`,
    users: ALL,
    errorRate: 0.12,
    errors: ['Contraseña incorrecta', 'Código MFA inválido'],
    descripcion: 'Inicio de sesión con verificación MFA.',
  },
  { weight: 11, accion: 'Cierre de sesión', recurso: () => `Sesión S-${hex()}`, users: ALL, descripcion: 'Cierre de sesión.' },
  {
    weight: 10,
    accion: 'Consultar',
    recurso: () => `Documento DOC-55${n2()}1`,
    users: [2, 3, 4, 7],
    descripcion: 'Consulta de documento clínico adjunto a un episodio.',
  },
  {
    weight: 8,
    accion: 'Consultar',
    recurso: () => `Episodio EP-2026-09${n2()}`,
    users: [2, 3, 4, 5, 6],
    descripcion: 'Consulta del resumen del episodio.',
  },
  {
    weight: 6,
    accion: 'Crear',
    recurso: () => `Paciente NN-04${n2()}`,
    users: [5, 6],
    errorRate: 0.08,
    errors: ['Validación: falta identificación provisional'],
    descripcion: 'Registro de paciente no identificado desde la unidad prehospitalaria.',
    cambios: (r) => [{ campo: 'Identificación', antes: '—', despues: `${r.slice(9)} (no identificado)` }],
  },
  {
    weight: 6,
    accion: 'Actualizar',
    recurso: () => `Paciente 402-11${n2()}345-2`,
    users: [2, 3, 4, 7],
    errorRate: 0.06,
    errors: ['Conflicto de versión (409): otro usuario editó el registro'],
    descripcion: 'Actualización de alergias del paciente.',
    cambios: () => [{ campo: 'Alergias', antes: 'Ninguna conocida', despues: 'Penicilina' }],
  },
  {
    weight: 4,
    accion: 'Crear',
    recurso: () => `Invitación INV-20${n2()}`,
    users: [0, 1, 4],
    descripcion: 'Invitación enviada por email.',
    cambios: () => [{ campo: 'Estado', antes: '—', despues: 'Pendiente' }],
  },
  {
    weight: 2,
    accion: 'Revocar',
    recurso: () => `Invitación INV-20${n2()}`,
    users: ADM,
    descripcion: 'Invitación revocada antes de ser aceptada.',
    cambios: () => [{ campo: 'Estado', antes: 'Pendiente', despues: 'Revocada' }],
  },
  {
    weight: 3,
    accion: 'Actualizar',
    recurso: () => `Usuario USR-0${n2()}`,
    users: ADM,
    descripcion: 'Cambio de rol de un usuario.',
    cambios: () => [{ campo: 'Rol', antes: 'Coordinador', despues: 'Médico' }],
  },
  {
    weight: 2,
    accion: 'Revocar',
    recurso: () => `Sesión S-${hex()}`,
    users: ADM,
    descripcion: 'Revocación de la sesión de otro usuario desde el portal.',
  },
  {
    weight: 2,
    accion: 'Crear',
    recurso: () => `Dispositivo DEV-01${n2()}`,
    users: ADM,
    descripcion: 'Alta de dispositivo con credencial de un solo uso.',
    cambios: () => [{ campo: 'Estado', antes: '—', despues: 'Pendiente' }],
  },
  {
    weight: 1,
    accion: 'Revocar',
    recurso: () => `Dispositivo DEV-01${n2()}`,
    users: ADM,
    descripcion: 'Credencial de dispositivo revocada.',
    cambios: () => [{ campo: 'Estado', antes: 'Activo', despues: 'Revocado' }],
  },
  {
    weight: 1,
    accion: 'Actualizar',
    recurso: () => 'Rol Auditor externo',
    users: ADM,
    descripcion: 'Cambio en la matriz de permisos.',
    cambios: () => [{ campo: 'Usuarios · Leer', antes: 'No', despues: 'Sí' }],
  },
  {
    weight: 2,
    accion: 'Eliminar',
    recurso: () => `Catálogo Medicamentos · MED-02${n2()}`,
    users: ADM,
    descripcion: 'Elemento de catálogo dado de baja (borrado lógico).',
    cambios: () => [{ campo: 'Estado', antes: 'Activo', despues: 'Eliminado (lógico)' }],
  },
  {
    weight: 2,
    accion: 'Actualizar',
    recurso: () => 'Catálogo CIE-10 · J18.9',
    users: ADM,
    descripcion: 'Edición de la descripción del código.',
    cambios: () => [{ campo: 'Descripción', antes: 'Neumonía', despues: 'Neumonía, no especificada' }],
  },
  {
    weight: 2,
    accion: 'Eliminar',
    recurso: () => `Paciente NN-03${n2()}`,
    users: [2, 3],
    errorRate: 1,
    errors: ['Permiso denegado (403): el rol Médico no puede eliminar pacientes'],
    descripcion: 'Intento de eliminar un registro de paciente.',
  },
  {
    weight: 2,
    accion: 'Exportar',
    recurso: () => 'Reporte de auditoría (CSV)',
    users: [0, 1, 9],
    descripcion: 'Exportación del registro de auditoría con filtros aplicados.',
  },
  {
    weight: 2,
    accion: 'Consultar',
    recurso: () => 'Registro de auditoría',
    users: [9],
    descripcion: 'Consulta del registro de auditoría.',
  },
  {
    weight: 1,
    accion: 'Actualizar',
    recurso: () => 'Configuración dashboard.autoRefresh',
    users: ADM,
    descripcion: 'Intervalo de actualización automática del dashboard.',
    cambios: () => [{ campo: 'autoRefresh', antes: '10 min', despues: '5 min' }],
  },
  {
    weight: 2,
    accion: 'Inicio de sesión',
    recurso: () => 'Sesión —',
    users: [10],
    errorRate: 1,
    errors: ['Cuenta suspendida'],
    descripcion: 'Intento de inicio de sesión de una cuenta suspendida.',
  },
];

const WEIGHTED = TEMPLATES.flatMap((t, i) => Array<number>(t.weight).fill(i));
const pad = (n: number) => String(n).padStart(2, '0');
const stamp = (d: Date) =>
  `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;

function generate(): AuditEvent[] {
  const out: AuditEvent[] = [];
  let t = MOCK_NOW.getTime() - 3 * 60_000;
  for (let i = 0; i < 240; i++) {
    const tpl = TEMPLATES[pick(WEIGHTED)];
    const [usuario, email, rol, userAgent, ip] = USERS[pick(tpl.users)];
    const recurso = tpl.recurso();
    const isError = !!tpl.errorRate && rnd() < tpl.errorRate;
    out.push({
      id: `EVT-${88412 - i * 7}`,
      timestamp: stamp(new Date(t)),
      usuario,
      email,
      rol,
      accion: tpl.accion,
      recurso,
      resultado: isError ? 'Error' : 'Éxito',
      error: isError ? pick(tpl.errors ?? ['Error']) : undefined,
      descripcion: tpl.descripcion,
      ip,
      userAgent,
      cambios: !isError && tpl.cambios ? tpl.cambios(recurso) : [],
      hash: `sha256:${hex().toLowerCase()}…${hex().toLowerCase().slice(0, 4)}`,
    });
    t -= Math.floor((18 + rnd() * 95) * 60_000) + Math.floor(rnd() * 59) * 1000;
  }
  return out;
}

export const AUDIT_EVENTS_MOCK: AuditEvent[] = generate();
