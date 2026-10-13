import { useState, type FormEvent } from 'react';
import { Button, FormError, Modal, Select } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { clinicsService, describeOrganizationError } from '../../clinics/services/clinicsService';
import { staffService } from '../services/staffService';
import type { ClinicRef } from '../types/staff.types';

/**
 * Afiliar el personal a una clínica (ClinicStaffAffiliation.Start, ahora). Solo se ofrecen clínicas activas en las que
 * no tenga ya una afiliación abierta; el backend lo vuelve a validar (409).
 */
export function AssignClinicModal({
  open,
  staffId,
  openClinics,
  onClose,
  onAssigned,
}: {
  open: boolean;
  staffId: string;
  openClinics: ClinicRef[];
  onClose: () => void;
  onAssigned: () => void;
}) {
  const clinics = useAsync(() => (open ? clinicsService.list({ status: 'Active' }) : Promise.resolve([])), [open]);
  const [clinicId, setClinicId] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const options = (clinics.data ?? [])
    .filter((c) => !openClinics.some((o) => o.id === c.id))
    .map((c) => ({ value: c.id, label: `${c.name} · ${c.municipality}` }));

  const close = () => {
    setClinicId('');
    setError(null);
    onClose();
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!clinicId || saving) return;
    setSaving(true);
    setError(null);
    try {
      await staffService.startAffiliation(staffId, clinicId);
      setClinicId('');
      onAssigned();
    } catch (err) {
      setError(describeOrganizationError(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={close}
      title="Asignar clínica"
      description="La afiliación empieza ahora. Un mismo personal puede estar afiliado a varias clínicas."
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancelar
          </Button>
          <Button type="submit" form="assign-form" loading={saving} loadingText="Asignando…" disabled={!clinicId}>
            Asignar
          </Button>
        </>
      }
    >
      <form id="assign-form" onSubmit={submit} className="flex flex-col gap-3">
        {(error || clinics.error) && <FormError message={error ?? clinics.error} />}
        <Select
          label="Clínica"
          value={clinicId}
          onChange={(e) => setClinicId(e.target.value)}
          options={options}
          placeholder={clinics.loading ? 'Cargando clínicas…' : options.length === 0 ? 'No hay clínicas activas disponibles' : 'Selecciona…'}
          disabled={clinics.loading || options.length === 0}
          required
        />
      </form>
    </Modal>
  );
}
