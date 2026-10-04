import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { Ban, Building2, CheckCircle2, Info, MapPin, Pencil, Trash2 } from 'lucide-react';
import type { Clinic } from '../../../types';
import { clinicsService, isServiceError } from '../../../services';
import { useCan } from '../../../auth';
import { useBreadcrumb } from '../../../components/common';
import { Button, Card, DetailList, EmptyState, ErrorState, LoadingSpinner, PageHeader, Tabs, tabIds } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { ClinicDevicesTab } from '../components/ClinicDevicesTab';
import { ClinicFormModal } from '../components/ClinicFormModal';
import { ClinicServicesPanel } from '../components/ClinicServicesPanel';
import { ClinicStaffTab } from '../components/ClinicStaffTab';
import { ClinicStatusBadge } from '../components/ClinicStatusBadge';
import { ClinicStatusDialog } from '../components/ClinicStatusDialog';
import { DeleteClinicDialog } from '../components/DeleteClinicDialog';

type TabKey = 'personal' | 'dispositivos' | 'servicios';
const TAB_PREFIX = 'clinic-resources';

/** Ficha de una clínica: datos de contacto, estado y pestañas de Personal, Dispositivos y Servicios. */
export function ClinicDetailPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const canUpdate = useCan('clinics', 'update');
  const canDelete = useCan('clinics', 'delete');

  // NOT_FOUND → `null` (404 con EmptyState); el resto de errores, ErrorState con Reintentar.
  const {
    data: clinic,
    loading,
    error,
    reload,
    setData,
  } = useAsync<Clinic | null>(
    () =>
      clinicsService.get(id).catch((e: unknown) => {
        if (isServiceError(e, 'NOT_FOUND')) return null;
        throw e;
      }),
    [id],
  );
  useBreadcrumb(clinic?.nombre);

  const [tab, setTab] = useState<TabKey>('personal');
  const [editing, setEditing] = useState(false);
  const [statusOpen, setStatusOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);

  if (loading && clinic === undefined) {
    return (
      <div className="flex justify-center py-16">
        <LoadingSpinner size="lg" label="Cargando clínica…" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex flex-col gap-5">
        <PageHeader title="Clínica" back={{ to: '/admin/clinicas', label: 'Volver a Clínicas' }} />
        <Card>
          <ErrorState title="No se pudo cargar la clínica" message={error} onRetry={reload} />
        </Card>
      </div>
    );
  }

  if (!clinic) {
    return (
      <Card className="mx-auto mt-6 w-full max-w-xl">
        <h1 className="sr-only">Clínica no encontrada</h1>
        <EmptyState
          icon={Building2}
          title="Clínica no encontrada"
          description={`La clínica «${id}» no existe o fue eliminada del directorio.`}
          action={
            <Link
              to="/admin/clinicas"
              className="inline-flex h-10 items-center rounded-lg bg-primary-600 px-4 text-sm font-semibold text-white hover:bg-primary-700 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-600"
            >
              Volver a Clínicas
            </Link>
          }
        />
      </Card>
    );
  }

  const active = clinic.estado === 'Activa';
  const tabs = [
    { value: 'personal' as const, label: 'Personal', count: clinic.personal },
    { value: 'dispositivos' as const, label: 'Dispositivos', count: clinic.dispositivos },
    { value: 'servicios' as const, label: 'Servicios' },
  ];
  const ids = tabIds(TAB_PREFIX, tab);

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        back={{ to: '/admin/clinicas', label: 'Volver a Clínicas' }}
        title={clinic.nombre}
        meta={<ClinicStatusBadge estado={clinic.estado} />}
        description={
          <>
            <span className="font-mono">{clinic.id}</span> · {clinic.tipo} · {clinic.direccion}, {clinic.provincia}
          </>
        }
        actions={
          <>
            {canUpdate && (
              <Button variant="secondary" leftIcon={<Pencil size={16} aria-hidden="true" />} onClick={() => setEditing(true)}>
                Editar
              </Button>
            )}
            {canUpdate && (
              <Button
                variant="secondary"
                leftIcon={active ? <Ban size={16} aria-hidden="true" /> : <CheckCircle2 size={16} aria-hidden="true" />}
                onClick={() => setStatusOpen(true)}
                className={active ? 'text-danger-text' : 'text-success-600'}
              >
                {active ? 'Desactivar' : 'Reactivar'}
              </Button>
            )}
            {canDelete && (
              <Button
                variant="secondary"
                aria-label="Eliminar clínica"
                title="Eliminar"
                leftIcon={<Trash2 size={16} aria-hidden="true" />}
                onClick={() => setDeleteOpen(true)}
              />
            )}
          </>
        }
      />

      {!active && (
        <div
          role="status"
          className="flex items-start gap-2.5 rounded-lg border border-line bg-surface-subtle px-4 py-3 text-[13px] text-ink-2"
        >
          <Info size={16} className="mt-0.5 shrink-0 text-ink-muted" aria-hidden="true" />
          <span>
            <strong className="font-semibold">Clínica inactiva.</strong> No aparece como destino de traslados. Su personal y dispositivos
            siguen asignados.
          </span>
        </div>
      )}

      <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <Card title="Información">
          <DetailList
            items={[
              { label: 'Tipo', value: clinic.tipo },
              {
                label: 'Dirección',
                value: (
                  <span className="flex flex-col">
                    {clinic.direccion}
                    <span className="text-[13px] text-ink-muted">{clinic.provincia}</span>
                  </span>
                ),
              },
              {
                label: 'Teléfono',
                mono: true,
                value: (
                  <a href={`tel:${clinic.telefono}`} className="text-primary-600 hover:underline">
                    {clinic.telefono}
                  </a>
                ),
              },
              {
                label: 'Email',
                value: (
                  <a href={`mailto:${clinic.email}`} className="text-primary-600 hover:underline">
                    {clinic.email}
                  </a>
                ),
              },
              { label: 'Contacto', value: clinic.contacto },
              { label: 'Horario', value: clinic.horario },
            ]}
          />
        </Card>
        <section
          aria-label="Ubicación en el mapa"
          className="relative flex min-h-[200px] flex-col items-center justify-center gap-2 overflow-hidden rounded-[10px] border border-line bg-[linear-gradient(135deg,#E1EAF4_25%,#EEF2F7_25%,#EEF2F7_50%,#E1EAF4_50%,#E1EAF4_75%,#EEF2F7_75%)] bg-[length:28px_28px] p-6 text-center"
        >
          <span className="flex items-center gap-2 rounded-full bg-white px-3 py-1.5 text-[13px] font-semibold text-ink shadow-sm">
            <MapPin size={16} className="text-danger-600" aria-hidden="true" />
            {clinic.nombre}
          </span>
          {clinic.lat !== undefined && clinic.lng !== undefined && (
            <span className="tabular font-mono text-xs text-ink-3">
              {clinic.lat.toFixed(4)}, {clinic.lng.toFixed(4)}
            </span>
          )}
          <span className="text-xs text-ink-muted">Mapa (placeholder · Azure Maps)</span>
        </section>
      </div>

      <Card padding="none" as="section" aria-label="Recursos de la clínica">
        <div className="overflow-x-auto px-2 pt-1 sm:px-4">
          <Tabs label="Recursos" idPrefix={TAB_PREFIX} tabs={tabs} value={tab} onChange={setTab} />
        </div>
        <div id={ids.panel} role="tabpanel" aria-labelledby={ids.tab} className="p-4 sm:p-5">
          {tab === 'personal' && <ClinicStaffTab clinicId={clinic.id} />}
          {tab === 'dispositivos' && <ClinicDevicesTab clinicId={clinic.id} />}
          {tab === 'servicios' && <ClinicServicesPanel clinicId={clinic.id} canUpdate={canUpdate} />}
        </div>
      </Card>

      <ClinicFormModal
        open={editing}
        clinic={clinic}
        onClose={() => setEditing(false)}
        onSaved={(c) => {
          setData(c);
          setEditing(false);
        }}
      />
      <ClinicStatusDialog
        clinic={statusOpen ? clinic : null}
        onClose={() => setStatusOpen(false)}
        onChanged={(c) => {
          setData(c);
          setStatusOpen(false);
        }}
      />
      <DeleteClinicDialog
        clinic={deleteOpen ? clinic : null}
        canDeactivate={canUpdate}
        onClose={() => setDeleteOpen(false)}
        onDeleted={() => navigate('/admin/clinicas', { replace: true })}
        onDeactivated={(c) => {
          setData(c);
          setDeleteOpen(false);
        }}
      />
    </div>
  );
}

export default ClinicDetailPage;
