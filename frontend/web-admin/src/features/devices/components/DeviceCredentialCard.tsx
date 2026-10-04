import { KeyRound } from 'lucide-react';
import type { Device } from '../../../types';
import { Card, DetailList } from '../../../components/shared';
import { CREDENTIAL_ROTATION_DAYS } from '../../../services';
import { cn } from '../../../utils/cn';
import { credentialAgeDays, formatDate } from '../helpers';

export interface DeviceCredentialCardProps {
  device: Device;
}

/** Huella pública de la credencial (nunca el secreto) y recordatorio de rotación cada 90 días. */
export function DeviceCredentialCard({ device }: DeviceCredentialCardProps) {
  const age = credentialAgeDays(device.credencial.emitidaEl);
  const left = CREDENTIAL_ROTATION_DAYS - age;
  const pct = Math.min(100, (age / CREDENTIAL_ROTATION_DAYS) * 100);
  return (
    <Card title="Credencial" actions={<KeyRound size={18} className="text-ink-muted" aria-hidden="true" />}>
      <div className="flex flex-col gap-4">
        <DetailList
          columns={1}
          items={[
            { label: 'Huella', value: device.credencial.huella, mono: true },
            { label: 'Emitida', value: `${formatDate(device.credencial.emitidaEl)} · hace ${age} ${age === 1 ? 'día' : 'días'}` },
          ]}
        />
        {device.estado !== 'Revocado' && (
          <div className="flex flex-col gap-1.5">
            <span className="text-xs font-medium text-ink-muted">Rotación recomendada cada {CREDENTIAL_ROTATION_DAYS} días</span>
            <span className="h-1.5 overflow-hidden rounded-full bg-line-soft" aria-hidden="true">
              <span
                className={cn('block h-full rounded-full', left <= 0 ? 'bg-danger-600' : left <= 15 ? 'bg-warning-500' : 'bg-success-600')}
                style={{ width: `${pct}%` }}
              />
            </span>
            <span
              className={cn('text-[13px]', left <= 0 ? 'font-semibold text-danger-text' : left <= 15 ? 'text-warning-700' : 'text-ink-3')}
            >
              {left <= 0 ? `Rotación vencida hace ${-left} días` : `Quedan ${left} días`}
            </span>
          </div>
        )}
      </div>
    </Card>
  );
}
