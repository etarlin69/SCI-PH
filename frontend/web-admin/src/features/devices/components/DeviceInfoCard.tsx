import { Link } from 'react-router-dom';
import type { Device } from '../../../types';
import { Card, DetailList } from '../../../components/shared';
import { formatDateTime, relativeTime } from '../helpers';
import { DeviceStatusBadge } from './DeviceStatusBadge';

export interface DeviceInfoCardProps {
  device: Device;
  clinicName?: string;
}

/** Tarjeta "Información" del detalle de dispositivo. */
export function DeviceInfoCard({ device: d, clinicName }: DeviceInfoCardProps) {
  return (
    <Card title="Información">
      <DetailList
        items={[
          { label: 'Nombre', value: d.nombre },
          { label: 'Tipo', value: d.tipo },
          { label: 'Estado', value: <DeviceStatusBadge estado={d.estado} /> },
          {
            label: 'Asignado a',
            value: d.clinicId ? (
              <>
                {d.asignadoA}
                {clinicName && (
                  <>
                    {' · '}
                    <Link to={`/admin/clinicas/${d.clinicId}`} className="font-medium text-primary-600 hover:underline">
                      {clinicName}
                    </Link>
                  </>
                )}
              </>
            ) : (
              d.asignadoA
            ),
          },
          { label: 'Fecha de creación', value: `${formatDateTime(d.creadoEl)} · por ${d.creadoPor}` },
          {
            label: 'Último acceso',
            value: d.ultimoAcceso ? (
              <span className="tabular">
                {formatDateTime(d.ultimoAcceso)} <span className="text-ink-muted">· {relativeTime(d.ultimoAcceso)}</span>
              </span>
            ) : (
              <span className="text-ink-muted">Esperando primera sincronización</span>
            ),
          },
          { label: 'IP último acceso', value: d.ultimaIp ?? '—', mono: true },
          { label: 'User-Agent', value: d.userAgent ?? '—', mono: true },
        ]}
      />
    </Card>
  );
}
