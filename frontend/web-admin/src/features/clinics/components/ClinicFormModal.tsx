import { useState, type FormEvent } from 'react';
import { Button, FormError, Input, Modal, Select } from '../../../components/shared';
import { clinicsService, describeOrganizationError } from '../services/clinicsService';
import type { ClinicAdmin, ClinicType } from '../types/clinics.types';
import { CLINIC_TYPE_LABELS } from './ClinicBadges';

const TYPE_OPTIONS = (Object.keys(CLINIC_TYPE_LABELS) as ClinicType[]).map((value) => ({ value, label: CLINIC_TYPE_LABELS[value] }));
const EMPTY = { name: '', type: '' as ClinicType | '', province: '', municipality: '', facilityCode: '', address: '', phoneNumber: '' };

/** Alta de clínica (Clinic.Register). Los campos son exactamente los del Domain; no hay edición posterior. */
export function ClinicFormModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (clinic: ClinicAdmin) => void }) {
  const [form, setForm] = useState(EMPTY);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = (field: keyof typeof EMPTY) => (value: string) => setForm((f) => ({ ...f, [field]: value }));
  const valid = Boolean(form.name.trim() && form.type && form.province.trim() && form.municipality.trim());
  const optional = (value: string) => value.trim() || null;

  const close = () => {
    setForm(EMPTY);
    setError(null);
    onClose();
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!valid || !form.type || saving) return;
    setSaving(true);
    setError(null);
    try {
      const clinic = await clinicsService.register({
        name: form.name.trim(),
        type: form.type,
        province: form.province.trim(),
        municipality: form.municipality.trim(),
        facilityCode: optional(form.facilityCode),
        address: optional(form.address),
        phoneNumber: optional(form.phoneNumber),
      });
      setForm(EMPTY);
      onCreated(clinic);
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
      title="Nueva clínica"
      description="Los datos no se pueden editar después del alta; la clínica sí puede activarse o desactivarse."
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancelar
          </Button>
          <Button type="submit" form="clinic-form" loading={saving} loadingText="Guardando…" disabled={!valid}>
            Crear clínica
          </Button>
        </>
      }
    >
      <form id="clinic-form" onSubmit={submit} className="flex flex-col gap-3">
        {error && <FormError message={error} />}
        <Input label="Nombre" value={form.name} onChange={(e) => set('name')(e.target.value)} required maxLength={200} />
        <Select
          label="Tipo"
          value={form.type}
          onChange={(e) => set('type')(e.target.value)}
          options={TYPE_OPTIONS}
          placeholder="Selecciona…"
          required
        />
        <div className="grid gap-3 sm:grid-cols-2">
          <Input label="Provincia" value={form.province} onChange={(e) => set('province')(e.target.value)} required maxLength={100} />
          <Input label="Municipio" value={form.municipality} onChange={(e) => set('municipality')(e.target.value)} required maxLength={100} />
        </div>
        <Input
          label="Código de establecimiento (opcional)"
          hint="Código de la autoridad sanitaria; único en la red."
          value={form.facilityCode}
          onChange={(e) => set('facilityCode')(e.target.value)}
          maxLength={80}
        />
        <Input label="Dirección (opcional)" value={form.address} onChange={(e) => set('address')(e.target.value)} maxLength={250} />
        <Input label="Teléfono (opcional)" type="tel" value={form.phoneNumber} onChange={(e) => set('phoneNumber')(e.target.value)} maxLength={32} />
      </form>
    </Modal>
  );
}
