import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Plus, SearchX, TabletSmartphone } from 'lucide-react';
import { useCan } from '../../../auth';
import { Button, EmptyState, ErrorState, PageHeader, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue';
import { devicesService } from '../../../services';
import type { Device, DeviceEnrollment, DeviceFilters as Filters } from '../../../types';
import { CreateDeviceModal } from '../components/CreateDeviceModal';
import { DeviceActionDialog } from '../components/DeviceActionDialog';
import { DeviceFilters } from '../components/DeviceFilters';
import { DevicesTable } from '../components/DevicesTable';
import { RotatedCredentialModal } from '../components/RotatedCredentialModal';
import { EMPTY_DEVICE_FILTERS, filterDevices } from '../filters';
import type { DeviceAction } from '../helpers';

/** Inventario de tablets, móviles y puestos autorizados a sincronizar. */
export function DevicesPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const canCreate = useCan('devices', 'create');
  const canUpdate = useCan('devices', 'update');
  const canDelete = useCan('devices', 'delete');
  const { data, loading, error, reload, setData } = useAsync(() => devicesService.list());
  const devices = useMemo(() => data ?? [], [data]);

  const [filters, setFilters] = useState<Filters>(EMPTY_DEVICE_FILTERS);
  const q = useDebouncedValue(filters.q);
  const filtered = useMemo(() => filterDevices(devices, { ...filters, q }), [devices, filters, q]);
  const [createOpen, setCreateOpen] = useState(false);
  const [action, setAction] = useState<{ kind: DeviceAction; device: Device } | null>(null);
  const [rotated, setRotated] = useState<DeviceEnrollment | null>(null);

  const runAction = async (kind: DeviceAction, d: Device) => {
    if (kind === 'rotate') {
      const result = await devicesService.rotateCredential(d.id);
      setData((prev) => prev?.map((x) => (x.id === d.id ? result.device : x)));
      setAction(null);
      setRotated(result);
    } else if (kind === 'revoke') {
      const updated = await devicesService.revoke(d.id);
      setData((prev) => prev?.map((x) => (x.id === d.id ? updated : x)));
      setAction(null);
      toast({ title: 'Credencial revocada', description: `${d.nombre} ya no puede sincronizar.`, tone: 'success' });
    } else {
      await devicesService.remove(d.id);
      setData((prev) => prev?.filter((x) => x.id !== d.id));
      setAction(null);
      toast({ title: 'Dispositivo eliminado', description: `${d.nombre}. Su historial se conserva en Auditoría.`, tone: 'success' });
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Dispositivos"
        description="Tablets de ambulancia, puestos de despacho y móviles autorizados para sincronizar con SCI-PH."
        actions={
          canCreate && (
            <Button leftIcon={<Plus size={16} aria-hidden="true" />} onClick={() => setCreateOpen(true)}>
              Nuevo dispositivo
            </Button>
          )
        }
      />
      <section aria-label="Lista de dispositivos" className="flex flex-col gap-4 rounded-[10px] border border-line bg-white p-4 sm:p-5">
        <DeviceFilters value={filters} onChange={setFilters} devices={devices} resultCount={filtered.length} />
        {error ? (
          <ErrorState
            title="No se pudieron cargar los dispositivos"
            message={`${error} Los equipos siguen sincronizando con normalidad.`}
            onRetry={reload}
          />
        ) : (
          <DevicesTable
            devices={filtered}
            loading={loading}
            canUpdate={canUpdate}
            canDelete={canDelete}
            onOpen={(d) => navigate(`/admin/dispositivos/${d.id}`)}
            onRotate={(device) => setAction({ kind: 'rotate', device })}
            onRevoke={(device) => setAction({ kind: 'revoke', device })}
            onDelete={(device) => setAction({ kind: 'delete', device })}
            resetPageKey={`${q}|${filters.tipo}|${filters.estado}`}
            emptyState={
              devices.length === 0 ? (
                <EmptyState
                  icon={TabletSmartphone}
                  title="No hay dispositivos registrados"
                  description="Registra la primera tablet o puesto de trabajo. Se vincula escaneando un código QR desde la app SCI-PH."
                  action={canCreate && <Button onClick={() => setCreateOpen(true)}>Nuevo dispositivo</Button>}
                />
              ) : (
                <EmptyState
                  icon={SearchX}
                  title="Ningún dispositivo coincide con los filtros"
                  action={
                    <Button variant="secondary" onClick={() => setFilters(EMPTY_DEVICE_FILTERS)}>
                      Limpiar filtros
                    </Button>
                  }
                />
              )
            }
          />
        )}
      </section>

      <CreateDeviceModal
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onCreated={(d) => {
          setCreateOpen(false);
          setData((prev) => [d, ...(prev ?? [])]);
          toast({
            title: 'Dispositivo registrado',
            description: `${d.nombre} aparecerá como «Pendiente» hasta su primera sincronización.`,
            tone: 'success',
          });
        }}
        onDiscarded={() => {
          setCreateOpen(false);
          toast({ title: 'Alta descartada', description: 'No se registró ningún dispositivo.', tone: 'info' });
        }}
      />
      <DeviceActionDialog action={action} onCancel={() => setAction(null)} onConfirm={runAction} />
      <RotatedCredentialModal
        enrollment={rotated}
        onDone={() => {
          setRotated(null);
          toast({ title: 'Credencial rotada', description: 'La credencial anterior ya no funciona.', tone: 'success' });
        }}
      />
    </div>
  );
}

export default DevicesPage;
