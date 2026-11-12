// DTOs de /api/web/admin/devices (SCI-PH.Application/DTOs/Security/DeviceModels.cs).

/** DeviceCredentialStatus (enum numérico en JSON). */
export const DeviceCredentialStatus = { Active: 1, Revoked: 2 } as const;
export type DeviceCredentialStatusCode = (typeof DeviceCredentialStatus)[keyof typeof DeviceCredentialStatus];

/** DeviceScopes del backend. */
export const DeviceScopes = {
  Heartbeat: 'Device.Heartbeat',
  FingerprintIdentify: 'Device.Fingerprint.Identify',
  FingerprintEnroll: 'Device.Fingerprint.Enroll',
} as const;
export type DeviceScope = (typeof DeviceScopes)[keyof typeof DeviceScopes];

export const DEVICE_SCOPE_LABELS: Record<DeviceScope, string> = {
  'Device.Heartbeat': 'Latido (heartbeat)',
  'Device.Fingerprint.Identify': 'Identificación por huella',
  'Device.Fingerprint.Enroll': 'Registro de huellas',
};

export interface DeviceSummary {
  id: string;
  deviceCode: string;
  name: string;
  scopes: string[];
  status: DeviceCredentialStatusCode;
  firmwareVersion: string | null;
  createdAtUtc: string;
  lastSeenAtUtc: string | null;
  rotatedAtUtc: string | null;
  revokedAtUtc: string | null;
}

export interface ProvisionDeviceRequest {
  deviceCode: string;
  name: string;
  scopes: string[];
}

/** Clave en claro del dispositivo: se devuelve UNA vez (alta o rotación) y el backend solo guarda su hash. */
export interface DeviceSecret {
  id: string;
  deviceCode: string;
  secret: string;
}

/** Misma regla que ProvisionDeviceRequest.DeviceCode. */
export const DEVICE_CODE_PATTERN = /^[A-Za-z0-9][A-Za-z0-9-]{2,59}$/;
