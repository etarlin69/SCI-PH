import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ChevronLeft, Power, PowerOff } from 'lucide-react';
import { Button, Card, ConfirmDialog, DetailList, ErrorState, LoadingSpinner, PageHeader, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { CLINIC_TYPE_LABELS, ClinicStatusBadge } from '../components/ClinicBadges';
import { clinicsService, describeOrganizationError } from '../services/clinicsService';

/**
 * Ficha de una clínica (`/admin/clinicas/:id`). Solo lectura de sus datos y cambio de estado: el Domain no permite
 * editarlos ni borrar la clínica. Desactivarla no termina las afiliaciones del personal.
 */
export function ClinicDetailPage() {
  const { id = '' } = useParams();
  const toast = useToast();
  const q = useAsync(() => clinicsService.get(id), [id]);
  const [confirming, setConfirming] = useState(false);
  const clinic = q.data;

  const toggle = async () => {
    if (!clinic) return;
    const deactivating = clinic.status === 'Active';
    try {
      if (deactivating) await clinicsService.deactivate(clinic.id);
      else await clinicsService.activate(clinic.id);
      toast({ title: deactivating ? 'Clínica desactivada' : 'Clínica activada', description: clinic.name, tone: 'success' });
      q.reload();
    } catch (e) {
      toast({ title: 'No se pudo cambiar el estado', description: describeOrganizationError(e), tone: 'error' });
    } finally {
      setConfirming(false);
    }
  };

  if (q.loading && !clinic) {
    return (
      <div className="flex justify-center py-16">
        <LoadingSpinner label="Cargando clínica…" />
      </div>
    );
  }

  if (q.error || !clinic) {
    return <ErrorState message={q.error ?? 'La clínica no existe.'} onRetry={q.reload} />;
  }

  const active = clinic.status === 'Active';
  return (
    <div className="flex flex-col gap-5">
      <Link to="/admin/clinicas" className="inline-flex w-fit items-center gap-1 text-sm text-ink-3 no-underline hover:text-ink">
        <ChevronLeft size={16} aria-hidden="true" />
        Clínicas
      </Link>
      <PageHeader
        title={clinic.name}
        description={CLINIC_TYPE_LABELS[clinic.type]}
        actions={
          <Button
            variant={active ? 'danger' : 'primary'}
            leftIcon={active ? <PowerOff size={16} aria-hidden="true" /> : <Power size={16} aria-hidden="true" />}
            onClick={() => setConfirming(true)}
          >
            {active ? 'Desactivar' : 'Activar'}
          </Button>
        }
      />
      <Card title="Datos de la clínica" actions={<ClinicStatusBadge status={clinic.status} />}>
        <DetailList
          columns={2}
          items={[
            { label: 'Tipo', value: CLINIC_TYPE_LABELS[clinic.type] },
            { label: 'Código de establecimiento', value: clinic.facilityCode ?? '—', mono: Boolean(clinic.facilityCode) },
            { label: 'Provincia', value: clinic.province },
            { label: 'Municipio', value: clinic.municipality },
            { label: 'Dirección', value: clinic.address ?? '—' },
            { label: 'Teléfono', value: clinic.phoneNumber ?? '—' },
            { label: 'Personal con afiliación abierta', value: clinic.activeStaffCount },
          ]}
        />
      </Card>
      <ConfirmDialog
        open={confirming}
        title={active ? 'Desactivar clínica' : 'Activar clínica'}
        message={
          active
            ? 'La clínica dejará de ser un contexto clínico válido para su personal. Las afiliaciones se conservan y no se terminan.'
            : 'La clínica volverá a ser un contexto clínico válido para el personal con afiliación abierta.'
        }
        confirmLabel={active ? 'Desactivar' : 'Activar'}
        tone={active ? 'danger' : 'primary'}
        onConfirm={toggle}
        onCancel={() => setConfirming(false)}
      />
    </div>
  );
}

export default ClinicDetailPage;
