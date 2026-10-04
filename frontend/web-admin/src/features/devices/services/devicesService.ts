import { DEVICE_ACCESSES_MOCK, DEVICES_MOCK } from '../../../data/mock';
import type { CreateDeviceInput, Device, DeviceAccess, DeviceEnrollment } from '../../../types';
import { getActor } from '../../../services/core/actor';
import { ServiceError } from '../../../services/core/errors';
import { mockList, mockRead, mockWrite, notFound, nowDateTime } from '../../../services/core/mock';
import { record } from '../../audit/services/auditService';

/** Minutos de validez del código de vinculación (QR/secreto). */
export const ENROLLMENT_TTL_MINUTES = 15;
/** Días tras los que se recomienda rotar la credencial. */
export const CREDENTIAL_ROTATION_DAYS = 90;

const devices: Device[] = [...DEVICES_MOCK];
let accesses: DeviceAccess[] = [...DEVICE_ACCESSES_MOCK];

function find(id: string): Device {
  return devices.find((d) => d.id === id) ?? notFound('El dispositivo');
}

function generateSecret(): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const values = new Uint32Array(32);
  crypto.getRandomValues(values);
  return Array.from(values, (v) => alphabet[v % 32]).join('');
}

function enrollment(device: Device): DeviceEnrollment {
  const secret = generateSecret();
  return {
    device,
    secret,
    qrPayload: `sciph://enroll?device=${device.id}&secret=${secret}`,
    expiresAt: Date.now() + ENROLLMENT_TTL_MINUTES * 60_000,
  };
}

const PREFIX: Record<Device['tipo'], number> = { Tablet: 1, Escritorio: 2, Móvil: 3 };

export const devicesService = {
  list(): Promise<Device[]> {
    return mockList(() => devices);
  },

  get(id: string): Promise<Device> {
    return mockRead(() => find(id));
  },

  listAccesses(id: string): Promise<DeviceAccess[]> {
    return mockList(() => accesses.filter((a) => a.deviceId === id));
  },

  /** Alta: registra el dispositivo como "Pendiente" y devuelve el secreto de un solo uso. */
  create(input: CreateDeviceInput): Promise<DeviceEnrollment> {
    return mockWrite('devices.create', () => {
      if (devices.some((d) => d.nombre.toLowerCase() === input.nombre.trim().toLowerCase())) {
        throw new ServiceError('CONFLICT', 'Ya existe un dispositivo con ese nombre.', { field: 'nombre' });
      }
      const p = PREFIX[input.tipo];
      const max = devices.filter((d) => Number(d.id[5]) === p).reduce((m, d) => Math.max(m, Number(d.id.slice(4))), p * 100);
      const now = nowDateTime();
      const device: Device = {
        id: `DEV-0${max + 1}`,
        nombre: input.nombre.trim(),
        tipo: input.tipo,
        estado: 'Pendiente',
        asignadoA: input.asignadoA?.trim() || 'Sin asignar',
        ultimoAcceso: null,
        creadoEl: now,
        creadoPor: getActor().nombre,
        credencial: { huella: 'sk_…pend', emitidaEl: now.slice(0, 10) },
      };
      const result = enrollment(device);
      device.credencial.huella = `sk_…${result.secret.slice(-4)}`;
      devices.unshift(device);
      record({
        accion: 'Crear',
        recurso: `Dispositivo ${device.id}`,
        descripcion: 'Alta de dispositivo con credencial de un solo uso.',
        cambios: [{ campo: 'Estado', antes: '—', despues: 'Pendiente' }],
      });
      return result;
    });
  },

  discardEnrollment(id: string): Promise<void> {
    return mockWrite('devices.discard', () => {
      const d = find(id);
      if (d.estado !== 'Pendiente') throw new ServiceError('CONFLICT', 'El dispositivo ya está vinculado.');
      devices.splice(devices.indexOf(d), 1);
    });
  },

  /** Genera una credencial nueva; la anterior deja de funcionar y hay que revincular. */
  rotateCredential(id: string): Promise<DeviceEnrollment> {
    return mockWrite('devices.rotate', () => {
      const d = find(id);
      if (d.estado !== 'Activo') throw new ServiceError('CONFLICT', 'Solo se puede rotar la credencial de un dispositivo activo.');
      const result = enrollment(d);
      d.credencial = { huella: `sk_…${result.secret.slice(-4)}`, emitidaEl: new Date().toISOString().slice(0, 10) };
      accesses.unshift({
        id: `${id}-R${Date.now()}`,
        deviceId: id,
        fecha: nowDateTime(),
        ip: '10.10.1.2',
        evento: `Rotación de credencial (${getActor().nombre})`,
        resultado: 'Éxito',
      });
      record({ accion: 'Actualizar', recurso: `Dispositivo ${id}`, descripcion: 'Credencial rotada.' });
      return { ...result, device: d };
    });
  },

  revoke(id: string): Promise<Device> {
    return mockWrite('devices.revoke', () => {
      const d = find(id);
      const antes = d.estado;
      d.estado = 'Revocado';
      d.revocadoEl = nowDateTime();
      accesses.unshift({
        id: `${id}-X${Date.now()}`,
        deviceId: id,
        fecha: d.revocadoEl,
        ip: '10.10.1.2',
        evento: `Credencial revocada (${getActor().nombre})`,
        resultado: 'Éxito',
      });
      record({
        accion: 'Revocar',
        recurso: `Dispositivo ${id}`,
        descripcion: 'Credencial de dispositivo revocada.',
        cambios: [{ campo: 'Estado', antes, despues: 'Revocado' }],
      });
      return d;
    });
  },

  remove(id: string): Promise<void> {
    return mockWrite('devices.remove', () => {
      const d = find(id);
      devices.splice(devices.indexOf(d), 1);
      accesses = accesses.filter((a) => a.deviceId !== id);
      record({ accion: 'Eliminar', recurso: `Dispositivo ${id}`, descripcion: `${d.nombre} eliminado del inventario.` });
    });
  },
};
