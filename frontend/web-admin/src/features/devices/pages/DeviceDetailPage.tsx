import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { RefreshCw, ShieldOff, TabletSmartphone, Trash2 } from 'lucide-react';
import { useCan } from '../../../auth';
import { useBreadcrumb } from '../../../components/common';
import { Button, EmptyState, ErrorState, LoadingSpinner, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { clinicsService, devicesService } from '../../../services';
import type { Device, DeviceEnrollment } from '../../../types';
import { DeviceAccessTable } from '../components/DeviceAccessTable';
import { DeviceActionDialog } from '../components/DeviceActionDialog';
import { DeviceCredentialCard } from '../components/DeviceCredentialCard';
import { DeviceInfoCard } from '../components/DeviceInfoCard';
import { DeviceStatusBadge } from '../components/DeviceStatusBadge';
import { DeviceTypeIcon } from '../components/DeviceTypeIcon';
import { RotatedCredentialModal } from '../components/RotatedCredentialModal';
import { formatDateTime, rotateBlockedReason, type DeviceAction } from '../helpers';

/** Ficha de dispositivo (`/admin/dispositivos/:id`): información, credencial e histórico de accesos. */
export function DeviceDetailPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const canUpdate = useCan('devices', 'update');
  const canDelete = useCan('devices', 'delete');

  const deviceQ = useAsync(() => devicesService.get(id), [id]);
  const accessQ = useAsync(() => devicesService.listAccesses(id), [id]);
  const clinicsQ = useAsync(() => clinicsService.options());
  const device = deviceQ.data;
  useBreadcrumb(device?.nombre);

  const [action, setAction] = useState<{ kind: DeviceAction; device: Device } | null>(null);
  const [rotated, setRotated] = useState<DeviceEnrollment | null>(null);
  const [deleted, setDeleted] = useState<Device | null>(null);

  if (deleted) {
    return (
      <EmptyState
        icon={Trash2}
        titleAs="h1"
        title="Dispositivo eliminado"
        description={`${deleted.nombre} (${deleted.id}) ya no puede conectarse. Su historial de accesos se conserva en Auditoría.`}
        action={
          <>
            <Link
              to="/admin/auditoria"
              className="inline-flex h-10 items-center rounded-lg border border-line px-4 text-sm font-semibold text-ink-2 hover:bg-surface-hover"
            >
              Ver en Auditoría
            </Link>
            <Link
              to="/admin/dispositivos"
              className="inline-flex h-10 items-center rounded-lg bg-primary-600 px-4 text-sm font-semibold text-white hover:bg-primary-700"
            >
              Volver a Dispositivos
            </Link>
          </>
        }
      />
    );
  }
  if (deviceQ.loading) {
    return (
      <div className="flex justify-center py-24">
        <LoadingSpinner size="lg" label="Cargando dispositivo…" />
      </div>
    );
  }
  if (deviceQ.error || !device) {
    return deviceQ.error?.includes('no existe') ? (
      <EmptyState
        icon={TabletSmartphone}
        titleAs="h1"
        title="Dispositivo no encontrado"
        description="No existe o fue eliminado."
        action={
          <Link to="/admin/dispositivos" className="font-semibold text-primary-600 hover:underline">
            Volver a Dispositivos
          </Link>
        }
      />
    ) : (
      <ErrorState title="No se pudo cargar el dispositivo" message={deviceQ.error ?? ''} onRetry={deviceQ.reload} />
    );
  }

  const blocked = rotateBlockedReason(device.estado);
  const clinicName = clinicsQ.data?.find((c) => c.id === device.clinicId)?.nombre;

  const runAction = async (kind: DeviceAction, d: Device) => {
    if (kind === 'rotate') {
      const result = await devicesService.rotateCredential(d.id);
      deviceQ.setData(result.device);
      setAction(null);
      setRotated(result);
      accessQ.reload();
    } else if (kind === 'revoke') {
      deviceQ.setData(await devicesService.revoke(d.id));
      setAction(null);
      accessQ.reload();
      toast({ title: 'Credencial revocada', description: `${d.nombre} ya no puede sincronizar.`, tone: 'success' });
    } else {
      await devicesService.remove(d.id);
      setAction(null);
      setDeleted(d);
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <Link to="/admin/dispositivos" className="w-fit text-[13px] font-medium text-primary-600 hover:underline">
        ← Dispositivos
      </Link>
      <header className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex min-w-0 items-center gap-3.5">
          <DeviceTypeIcon tipo={device.tipo} size={22} boxed />
          <div className="flex min-w-0 flex-col gap-1">
            <div className="flex flex-wrap items-center gap-2.5">
              <h1 className="truncate text-xl font-semibold text-ink sm:text-2xl">{device.nombre}</h1>
              <DeviceStatusBadge estado={device.estado} />
            </div>
            <p className="text-[13px] text-ink-muted">
              <span className="font-mono">{device.id}</span> · {device.tipo} · {device.asignadoA}
            </p>
          </div>
        </div>
        {(canUpdate || canDelete) && (
          <div className="flex flex-wrap gap-2">
            {canUpdate && (
              <>
                <Button
                  variant="secondary"
                  leftIcon={<RefreshCw size={16} aria-hidden="true" />}
                  disabled={!!blocked}
                  title={blocked}
                  onClick={() => setAction({ kind: 'rotate', device })}
                >
                  Rotar credencial
                </Button>
                <Button
                  variant="danger"
                  leftIcon={<ShieldOff size={16} aria-hidden="true" />}
                  disabled={device.estado === 'Revocado'}
                  onClick={() => setAction({ kind: 'revoke', device })}
                >
                  Revocar
                </Button>
              </>
            )}
            {canDelete && (
              <Button
                variant="ghost"
                leftIcon={<Trash2 size={16} aria-hidden="true" />}
                onClick={() => setAction({ kind: 'delete', device })}
              >
                Eliminar
              </Button>
            )}
          </div>
        )}
      </header>

      {device.estado === 'Revocado' && (
        <div role="status" className="rounded-lg border border-danger-600/30 bg-danger-50 px-4 py-3 text-[13px] text-danger-text">
          <strong>Credencial revocada{device.revocadoEl ? ` el ${formatDateTime(device.revocadoEl)}` : ''}.</strong> El dispositivo no puede
          sincronizar. Para volver a usarlo, elimínalo y regístralo de nuevo.
        </div>
      )}

      <div className="grid grid-cols-1 gap-5 xl:grid-cols-[2fr_1fr]">
        <DeviceInfoCard device={device} clinicName={clinicName} />
        <DeviceCredentialCard device={device} />
      </div>
      <DeviceAccessTable accesses={accessQ.data ?? []} loading={accessQ.loading} error={accessQ.error} onRetry={accessQ.reload} />

      <DeviceActionDialog action={action} onCancel={() => setAction(null)} onConfirm={runAction} />
      <RotatedCredentialModal
        enrollment={rotated}
        onDone={() => {
          setRotated(null);
          toast({ title: 'Credencial rotada', description: 'La credencial anterior ya no funciona.', tone: 'success' });
          navigate(`/admin/dispositivos/${device.id}`, { replace: true });
        }}
      />
    </div>
  );
}

export default DeviceDetailPage;
