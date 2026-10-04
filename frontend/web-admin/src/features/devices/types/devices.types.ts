import type { DateTime, ISODate } from '../../../types/common';

export type DeviceType = 'Tablet' | 'Móvil' | 'Escritorio';
export type DeviceStatus = 'Activo' | 'Pendiente' | 'Revocado';

export interface DeviceCredential {
  huella: string;
  emitidaEl: ISODate;
}

export interface Device {
  id: string;
  nombre: string;
  tipo: DeviceType;
  estado: DeviceStatus;
  asignadoA: string;
  clinicId?: string;
  ultimoAcceso: DateTime | null;
  ultimaIp?: string;
  userAgent?: string;
  creadoEl: DateTime;
  creadoPor: string;
  credencial: DeviceCredential;
  revocadoEl?: DateTime;
}

export type DeviceAccessResult = 'Éxito' | 'Fallido';

export interface DeviceAccess {
  id: string;
  deviceId: string;
  fecha: DateTime;
  ip: string;
  evento: string;
  resultado: DeviceAccessResult;
  detalle?: string;
}

export interface CreateDeviceInput {
  nombre: string;
  tipo: DeviceType;
  asignadoA?: string;
}

export interface DeviceEnrollment {
  device: Device;
  secret: string;
  qrPayload: string;
  expiresAt: number;
}

export interface DeviceFilters {
  q: string;
  tipo: DeviceType | '';
  estado: DeviceStatus | '';
}
