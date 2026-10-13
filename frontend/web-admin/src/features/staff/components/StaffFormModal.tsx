import { useState, type FormEvent } from 'react';
import { Button, FormError, Input, Modal, Select } from '../../../components/shared';
import { describeOrganizationError } from '../../clinics/services/clinicsService';
import { staffService } from '../services/staffService';
import type { ClinicalStaffAdmin, ClinicalStaffType } from '../types/staff.types';
import { STAFF_TYPE_LABELS } from './StaffBadges';

const TYPE_OPTIONS = (Object.keys(STAFF_TYPE_LABELS) as ClinicalStaffType[]).map((value) => ({ value, label: STAFF_TYPE_LABELS[value] }));
const EMPTY = { givenNames: '', familyNames: '', type: '' as ClinicalStaffType | '', license: '', specialty: '', phoneNumber: '' };

/**
 * Alta de personal clínico (ClinicalStaff.Register). Puede existir sin cuenta: la cuenta se crea después con la
 * invitación. No se pide email: pertenece a la invitación, no al personal clínico.
 */
export function StaffFormModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (staff: ClinicalStaffAdmin) => void }) {
  const [form, setForm] = useState(EMPTY);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = (field: keyof typeof EMPTY) => (value: string) => setForm((f) => ({ ...f, [field]: value }));
  const valid = Boolean(form.givenNames.trim() && form.familyNames.trim() && form.type);
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
      const staff = await staffService.register({
        givenNames: form.givenNames.trim(),
        familyNames: form.familyNames.trim(),
        type: form.type,
        professionalLicenseNumber: optional(form.license),
        specialty: optional(form.specialty),
        phoneNumber: optional(form.phoneNumber),
      });
      setForm(EMPTY);
      onCreated(staff);
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
      title="Nuevo personal clínico"
      description="Se crea sin cuenta. Después podrás afiliarlo a clínicas e invitarlo para que active su acceso."
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancelar
          </Button>
          <Button type="submit" form="staff-form" loading={saving} loadingText="Guardando…" disabled={!valid}>
            Crear personal
          </Button>
        </>
      }
    >
      <form id="staff-form" onSubmit={submit} className="flex flex-col gap-3">
        {error && <FormError message={error} />}
        <div className="grid gap-3 sm:grid-cols-2">
          <Input label="Nombres" value={form.givenNames} onChange={(e) => set('givenNames')(e.target.value)} required maxLength={120} />
          <Input label="Apellidos" value={form.familyNames} onChange={(e) => set('familyNames')(e.target.value)} required maxLength={120} />
        </div>
        <Select
          label="Tipo de personal"
          value={form.type}
          onChange={(e) => set('type')(e.target.value)}
          options={TYPE_OPTIONS}
          placeholder="Selecciona…"
          required
        />
        <Input
          label="Número de licencia profesional (opcional)"
          hint="Único en la red."
          value={form.license}
          onChange={(e) => set('license')(e.target.value)}
          maxLength={80}
        />
        <Input label="Especialidad (opcional)" value={form.specialty} onChange={(e) => set('specialty')(e.target.value)} maxLength={120} />
        <Input label="Teléfono (opcional)" type="tel" value={form.phoneNumber} onChange={(e) => set('phoneNumber')(e.target.value)} maxLength={32} />
      </form>
    </Modal>
  );
}
