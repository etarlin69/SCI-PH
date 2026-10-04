import type { Device, DeviceAccess, DeviceStatus, DeviceType } from '../../types';
import { ago, at, daysAgo, seededRandom } from './time';

type Row = [
  id: string,
  nombre: string,
  tipo: DeviceType,
  estado: DeviceStatus,
  asignadoA: string,
  clinicId: string | undefined,
  ultimoAcceso: string | null,
  ip?: string,
];

const UA_TABLET = 'SCI-PH Móvil 3.2.1 (Android 14; Samsung SM-X200; build 3210)';
const UA_DESKTOP = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/129.0 SCI-PH-Desktop/2.4';
const UA_PHONE = 'SCI-PH Móvil 3.2.1 (iOS 18.0; iPhone 14)';

const ROWS: Row[] = [
  ['DEV-0107', 'Tablet AMB-07', 'Tablet', 'Activo', 'Unidad AMB-07 · Clínica Central', 'CLI-001', ago({ minutes: 9 }), '10.20.8.112'],
  ['DEV-0103', 'Tablet AMB-03', 'Tablet', 'Activo', 'Unidad AMB-03 · Hospital del Norte', 'CLI-002', ago({ minutes: 40 }), '10.20.8.103'],
  ['DEV-0110', 'Tablet AMB-10', 'Tablet', 'Revocado', 'Unidad AMB-10 · reportada como robada', 'CLI-001', at(12, '03:12'), '10.20.8.110'],
  ['DEV-0118', 'Tablet AMB-18', 'Tablet', 'Pendiente', 'Unidad AMB-18 · Clínica Sur', 'CLI-003', null],
  ['DEV-0201', 'Puesto despacho 1', 'Escritorio', 'Activo', 'Central de despacho', 'CLI-001', ago({ minutes: 2 }), '10.10.6.3'],
  ['DEV-0203', 'Puesto despacho 3', 'Escritorio', 'Pendiente', 'Central de despacho', 'CLI-001', null],
  ['DEV-0210', 'Triaje Emergencias', 'Escritorio', 'Activo', 'Clínica Central · Emergencias', 'CLI-001', ago({ hours: 1 }), '10.10.2.80'],
  ['DEV-0213', 'Estación UCI', 'Escritorio', 'Activo', 'Clínica Central · UCI', 'CLI-001', ago({ minutes: 28 }), '10.10.2.91'],
  [
    'DEV-0301',
    'Móvil supervisor turno A',
    'Móvil',
    'Activo',
    'Coordinación · Hospital del Norte',
    'CLI-002',
    ago({ hours: 2, minutes: 45 }),
    '10.30.1.21',
  ],
  [
    'DEV-0305',
    'Móvil guardia pediatría',
    'Móvil',
    'Revocado',
    'Hospital Infantil Sur · Pediatría',
    'CLI-005',
    at(16, '07:15'),
    '10.30.5.14',
  ],
];

const UA: Record<DeviceType, string> = { Tablet: UA_TABLET, Escritorio: UA_DESKTOP, Móvil: UA_PHONE };

export const DEVICES_MOCK: Device[] = ROWS.map(([id, nombre, tipo, estado, asignadoA, clinicId, ultimoAcceso, ip], i) => ({
  id,
  nombre,
  tipo,
  estado,
  asignadoA,
  clinicId,
  ultimoAcceso,
  ultimaIp: ip,
  userAgent: ultimoAcceso ? UA[tipo] : undefined,
  creadoEl: at(200 - i * 9, '09:00'),
  creadoPor: 'Admin SCIH',
  credencial: { huella: `sk_…${(0x9f2a + i * 4099).toString(16).toUpperCase().slice(-4)}`, emitidaEl: daysAgo(56 - i * 3) },
  revocadoEl: estado === 'Revocado' ? (ultimoAcceso ?? undefined) : undefined,
}));

export const DEVICE_ACCESSES_MOCK: DeviceAccess[] = DEVICES_MOCK.filter((d) => d.ultimoAcceso).flatMap((d, di) => {
  const rnd = seededRandom(1000 + di);
  const out: DeviceAccess[] = [];
  let minutes = d.estado === 'Revocado' ? 60 * 24 * 12 : 9;
  for (let i = 0; i < 24; i++) {
    const failed = i === 9 || i === 17;
    out.push({
      id: `${d.id}-A${i}`,
      deviceId: d.id,
      fecha: ago({ minutes }),
      ip: i === 17 ? '190.166.10.4' : (d.ultimaIp ?? '10.0.0.1'),
      evento: i % 7 === 3 ? 'Autenticación (token renovado)' : i === 17 ? 'Autenticación' : 'Sincronización de registros',
      resultado: failed ? 'Fallido' : 'Éxito',
      detalle: i === 9 ? 'Tiempo de espera agotado · se reintentó' : i === 17 ? 'Firma inválida (credencial anterior)' : undefined,
    });
    minutes += 25 + Math.floor(rnd() * 16);
  }
  return out;
});
