import { useState, type FormEvent } from 'react';
import { INVITABLE_STAFF_ROLES, ROLE_LABELS, type SecurityRole } from '../../../auth';
import { Button, FormError, Input, Modal, Select } from '../../../components/shared';
import { describeInvitationError, invitationsService } from '../../invitations/services/invitationsService';
import type { ClinicalStaffAdmin } from '../types/staff.types';
import { SUGGESTED_ROLE, staffFullName } from './StaffBadges';

/**
 * Invitar al personal a crear su cuenta, con el flujo de invitaciones existente
 * (POST /api/web/admin/invitations/staff, Users.Manage). El id del personal se envía internamente; el email es de la
 * invitación (no se guarda en el personal clínico). Al activarla, la cuenta queda vinculada a este personal.
 */
export function InviteStaffModal({ staff, open, onClose, onInvited }: { staff: ClinicalStaffAdmin; open: boolean; onClose: () => void; onInvited: (email: string) => void }) {
  const suggested = SUGGESTED_ROLE[staff.type] ?? '';
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<SecurityRole | ''>(suggested);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const valid = Boolean(email.trim() && role);

  const close = () => {
    setEmail('');
    setRole(suggested);
    setError(null);
    onClose();
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!valid || !role || saving) return;
    setSaving(true);
    setError(null);
    try {
      await invitationsService.inviteStaff({ clinicalStaffId: staff.id, email: email.trim(), role });
      const sentTo = email.trim();
      setEmail('');
      onInvited(sentTo);
    } catch (err) {
      setError(describeInvitationError(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={close}
      title="Invitar a crear su cuenta"
      description={`Se enviará un enlace de activación para ${staffFullName(staff)}. Al activarlo, la cuenta quedará vinculada.`}
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancelar
          </Button>
          <Button type="submit" form="invite-staff-form" loading={saving} loadingText="Enviando…" disabled={!valid}>
            Enviar invitación
          </Button>
        </>
      }
    >
      <form id="invite-staff-form" onSubmit={submit} className="flex flex-col gap-3">
        {error && <FormError message={error} />}
        <Input label="Email de la cuenta" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required maxLength={256} />
        <Select
          label="Rol de la cuenta"
          value={role}
          onChange={(e) => setRole(e.target.value as SecurityRole | '')}
          options={INVITABLE_STAFF_ROLES.map((r) => ({ value: r, label: ROLE_LABELS[r] }))}
          placeholder="Selecciona…"
          hint={suggested ? `Sugerido por el tipo de personal: ${ROLE_LABELS[suggested]}. Puedes elegir otro.` : 'Elige el rol de la cuenta.'}
          required
        />
      </form>
    </Modal>
  );
}
