import { apiRequest, describeApiError } from '../../../api';
import type { DeviceSecret, DeviceSummary, ProvisionDeviceRequest } from '../types/devices.types';

const BASE = '/api/web/admin/devices';

/** Credenciales de dispositivos ESP32 para la Device API (permiso Devices.Manage). No hay borrado: se revocan. */
export const devicesService = {
  list: (signal?: AbortSignal) => apiRequest<DeviceSummary[]>(BASE, { signal }),

  provision: (request: ProvisionDeviceRequest) => apiRequest<DeviceSecret>(BASE, { method: 'POST', body: request }),

  rotateSecret: (id: string) => apiRequest<DeviceSecret>(`${BASE}/${encodeURIComponent(id)}/rotate-secret`, { method: 'POST' }),

  revoke: (id: string) => apiRequest<void>(`${BASE}/${encodeURIComponent(id)}/revoke`, { method: 'POST' }),
};

export const describeDeviceError = (e: unknown) =>
  describeApiError(e, {
    400: 'Datos no válidos: elige al menos un permiso de dispositivo.',
    404: 'El dispositivo no existe.',
    409: 'Ese código de dispositivo ya está registrado.',
  });
