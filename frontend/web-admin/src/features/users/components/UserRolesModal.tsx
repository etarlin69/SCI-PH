import { useEffect, useState } from 'react';
import { ROLE_LABELS, SecurityRoles, type SecurityRole } from '../../../auth';
import { Button, Checkbox, FormError, Modal } from '../../../components/shared';

export interface UserRolesModalProps {
  open: boolean;
  userName: string;
  currentRoles: readonly string[];
  saving: boolean;
  error: string | null;
  onSave: (roles: SecurityRole[]) => void;
  onClose: () => void;
}

const STAFF_ROLES = SecurityRoles.filter((r) => r !== 'Patient');

/**
 * Asignación de roles predefinidos (los permisos de cada rol los fija el backend en RolePermissions).
 * Paciente es excluyente: no se combina con roles de personal.
 */
export function UserRolesModal({ open, userName, currentRoles, saving, error, onSave, onClose }: UserRolesModalProps) {
  const [selected, setSelected] = useState<Set<SecurityRole>>(new Set());

  useEffect(() => {
    if (open) setSelected(new Set(currentRoles.filter((r): r is SecurityRole => (SecurityRoles as readonly string[]).includes(r))));
  }, [open, currentRoles]);

  const isPatient = selected.has('Patient');
  const toggle = (role: SecurityRole) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(role)) next.delete(role);
      else if (role === 'Patient') return new Set<SecurityRole>(['Patient']);
      else {
        next.delete('Patient');
        next.add(role);
      }
      return next;
    });

  return (
    <Modal
      open={open}
      onClose={onClose}
      dismissible={!saving}
      title="Roles de la cuenta"
      description={`${userName}. Al guardar se cerrarán sus sesiones abiertas.`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Cancelar
          </Button>
          <Button onClick={() => onSave([...selected])} loading={saving} loadingText="Guardando…" disabled={selected.size === 0}>
            Guardar roles
          </Button>
        </>
      }
    >
      <fieldset className="flex flex-col gap-2.5">
        <legend className="mb-1 text-[13px] font-semibold text-ink-2">Personal</legend>
        {STAFF_ROLES.map((role) => (
          <Checkbox key={role} label={ROLE_LABELS[role]} checked={selected.has(role)} disabled={saving} onChange={() => toggle(role)} />
        ))}
      </fieldset>
      <fieldset className="mt-4 flex flex-col gap-2.5">
        <legend className="mb-1 text-[13px] font-semibold text-ink-2">Paciente</legend>
        <Checkbox
          label={ROLE_LABELS.Patient}
          description="Excluyente: quita los roles de personal."
          checked={isPatient}
          disabled={saving}
          onChange={() => toggle('Patient')}
        />
      </fieldset>
      <div className="mt-3">
        <FormError message={error} />
      </div>
    </Modal>
  );
}
