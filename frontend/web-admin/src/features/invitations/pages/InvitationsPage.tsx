import { useState, type FormEvent } from 'react';
import { Info } from 'lucide-react';
import { INVITABLE_STAFF_ROLES, Permissions, ROLE_LABELS, roleLabel, useAuth } from '../../../auth';
import { Badge, Button, Card, ConfirmDialog, FormError, Input, PageHeader, Select, Table, useToast, type Column } from '../../../components/shared';
import { formatUtcDateTime } from '../../../utils/dates';
import { EMAIL_PATTERN } from '../../auth/password';
import { GUID_PATTERN, describeInvitationError, invitationsService } from '../services/invitationsService';
import { InvitationSubjectType, type InvitationSummary } from '../types/invitations.types';

/**
 * Invitaciones de cuenta. Solo lo que el backend soporta hoy: invitar a un paciente o a un miembro del personal
 * ya registrados (por su id), reemitir y revocar. No hay listado en servidor: la tabla muestra solo las
 * invitaciones emitidas en esta pestaña y se pierde al recargar.
 */
export function InvitationsPage() {
  const { can } = useAuth();
  const canInvitePatient = can(Permissions.Accounts.InvitePatient);
  const canManage = can(Permissions.Users.Manage);
  const toast = useToast();
  const [issued, setIssued] = useState<InvitationSummary[]>([]);
  const [revokeTarget, setRevokeTarget] = useState<string | null>(null);

  const remember = (inv: InvitationSummary) => setIssued((prev) => [inv, ...prev.filter((x) => x.id !== inv.id)]);

  const reissue = async (id: string) => {
    try {
      const inv = await invitationsService.reissue(id);
      remember(inv);
      toast({ title: 'Invitación reemitida', description: inv.emailSent ? `Enviada a ${inv.email}` : 'No se pudo enviar el email.', tone: 'success' });
    } catch (e) {
      toast({ title: 'No se pudo reemitir', description: describeInvitationError(e), tone: 'error' });
    }
  };

  const revoke = async () => {
    if (!revokeTarget) return;
    try {
      await invitationsService.revoke(revokeTarget);
      setIssued((prev) => prev.filter((x) => x.id !== revokeTarget));
      toast({ title: 'Invitación revocada', tone: 'success' });
    } catch (e) {
      toast({ title: 'No se pudo revocar', description: describeInvitationError(e), tone: 'error' });
    } finally {
      setRevokeTarget(null);
    }
  };

  const columns: Column<InvitationSummary>[] = [
    {
      id: 'subject',
      header: 'Destinatario',
      cell: (i) => (
        <span className="flex flex-col">
          <span className="font-semibold text-ink">{i.email}</span>
          <span className="text-xs text-ink-muted">{i.subjectType === InvitationSubjectType.Patient ? 'Paciente' : 'Personal'}</span>
        </span>
      ),
    },
    { id: 'role', header: 'Rol', cell: (i) => roleLabel(i.intendedRole) },
    { id: 'expires', header: 'Caduca', cell: (i) => <span className="tabular">{formatUtcDateTime(i.expiresAtUtc)}</span> },
    {
      id: 'email',
      header: 'Email',
      cell: (i) => <Badge tone={i.emailSent ? 'success' : 'warning'}>{i.emailSent ? 'Enviado' : 'No enviado'}</Badge>,
    },
    {
      id: 'actions',
      header: <span className="sr-only">Acciones</span>,
      align: 'right',
      cell: (i) =>
        canManage ? (
          <span className="flex justify-end gap-1.5">
            <Button size="sm" variant="secondary" onClick={() => void reissue(i.id)}>
              Reemitir
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setRevokeTarget(i.id)}>
              Revocar
            </Button>
          </span>
        ) : null,
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Invitaciones"
        description="Las cuentas se crean invitando a un paciente o a un miembro del personal ya registrados. La persona recibe un enlace para fijar su contraseña."
      />

      <p className="flex gap-2 rounded-lg border border-primary-300 bg-primary-50 px-3.5 py-2.5 text-[13px] text-primary-700">
        <Info size={16} className="mt-0.5 shrink-0" aria-hidden="true" />
        Pendiente de backend: listado de invitaciones y búsqueda de pacientes y personal. Por ahora se indica el identificador
        del registro.
      </p>

      <div className="grid gap-4 lg:grid-cols-2">
        {canManage && <InviteStaffForm onIssued={remember} />}
        {canInvitePatient && <InvitePatientForm onIssued={remember} />}
      </div>

      {canManage && <ManageByIdForm onReissue={reissue} onRevoke={setRevokeTarget} />}

      {issued.length > 0 && (
        <Card title="Emitidas en esta sesión" subtitle="No es el listado del servidor: desaparece al recargar la página." padding="none">
          <Table caption="Invitaciones emitidas en esta sesión" columns={columns} rows={issued} getRowId={(i) => i.id} />
        </Card>
      )}

      <ConfirmDialog
        open={revokeTarget !== null}
        title="Revocar invitación"
        message="El enlace dejará de funcionar. Podrás emitir una nueva invitación más tarde."
        confirmLabel="Revocar"
        onConfirm={revoke}
        onCancel={() => setRevokeTarget(null)}
      />
    </div>
  );
}

function InviteStaffForm({ onIssued }: { onIssued: (inv: InvitationSummary) => void }) {
  const toast = useToast();
  const [staffId, setStaffId] = useState('');
  const [email, setEmail] = useState('');
  const [role, setRole] = useState('');
  const [touched, setTouched] = useState(false);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const idError = touched && !GUID_PATTERN.test(staffId.trim()) ? 'Identificador no válido (GUID).' : undefined;
  const emailError = touched && !EMAIL_PATTERN.test(email.trim()) ? 'Email no válido.' : undefined;
  const roleError = touched && !role ? 'Selecciona un rol.' : undefined;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (sending || !GUID_PATTERN.test(staffId.trim()) || !EMAIL_PATTERN.test(email.trim()) || !role) return;
    setSending(true);
    setError(null);
    try {
      const inv = await invitationsService.inviteStaff({ clinicalStaffId: staffId.trim(), email: email.trim(), role });
      onIssued(inv);
      toast({ title: 'Invitación creada', description: inv.emailSent ? `Enviada a ${inv.email}` : 'No se pudo enviar el email; reemítela.', tone: 'success' });
      setStaffId('');
      setEmail('');
      setRole('');
      setTouched(false);
    } catch (err) {
      setError(describeInvitationError(err));
    } finally {
      setSending(false);
    }
  };

  return (
    <Card title="Invitar a personal" subtitle="Médico, enfermería, paramédico o recepción. El rol de administrador no se concede por invitación.">
      <form noValidate onSubmit={submit} className="flex flex-col gap-3.5">
        <Input label="Id del registro de personal (ClinicalStaff)" value={staffId} onChange={(e) => setStaffId(e.target.value)} error={idError} className="font-mono" required />
        <Input label="Email" type="email" autoComplete="off" value={email} maxLength={256} onChange={(e) => setEmail(e.target.value)} error={emailError} required />
        <Select
          label="Rol"
          value={role}
          onChange={(e) => setRole(e.target.value)}
          placeholder="Selecciona un rol…"
          options={INVITABLE_STAFF_ROLES.map((r) => ({ value: r, label: ROLE_LABELS[r] }))}
          error={roleError}
          required
        />
        <FormError message={error} />
        <Button type="submit" loading={sending} loadingText="Enviando…" className="w-full sm:w-fit">
          Enviar invitación
        </Button>
      </form>
    </Card>
  );
}

function InvitePatientForm({ onIssued }: { onIssued: (inv: InvitationSummary) => void }) {
  const toast = useToast();
  const [patientId, setPatientId] = useState('');
  const [touched, setTouched] = useState(false);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const idError = touched && !GUID_PATTERN.test(patientId.trim()) ? 'Identificador no válido (GUID).' : undefined;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (sending || !GUID_PATTERN.test(patientId.trim())) return;
    setSending(true);
    setError(null);
    try {
      const inv = await invitationsService.invitePatient({ patientId: patientId.trim() });
      onIssued(inv);
      toast({ title: 'Invitación creada', description: inv.emailSent ? `Enviada a ${inv.email}` : 'No se pudo enviar el email; reemítela.', tone: 'success' });
      setPatientId('');
      setTouched(false);
    } catch (err) {
      setError(describeInvitationError(err));
    } finally {
      setSending(false);
    }
  };

  return (
    <Card title="Invitar a un paciente" subtitle="Se usa el email activo registrado del paciente. Una invitación nueva reemplaza la pendiente.">
      <form noValidate onSubmit={submit} className="flex flex-col gap-3.5">
        <Input label="Id del paciente (Patient)" value={patientId} onChange={(e) => setPatientId(e.target.value)} error={idError} className="font-mono" required />
        <FormError message={error} />
        <Button type="submit" loading={sending} loadingText="Enviando…" className="w-full sm:w-fit">
          Enviar invitación
        </Button>
      </form>
    </Card>
  );
}

function ManageByIdForm({ onReissue, onRevoke }: { onReissue: (id: string) => Promise<void>; onRevoke: (id: string) => void }) {
  const [id, setId] = useState('');
  const [busy, setBusy] = useState(false);
  const valid = GUID_PATTERN.test(id.trim());

  return (
    <Card title="Gestionar una invitación existente" subtitle="Con el identificador de la invitación.">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
        <Input
          label="Id de la invitación"
          value={id}
          onChange={(e) => setId(e.target.value)}
          error={id && !valid ? 'Identificador no válido (GUID).' : undefined}
          className="font-mono"
          containerClassName="min-w-0 flex-1"
        />
        <div className="flex gap-2">
          <Button
            variant="secondary"
            disabled={!valid}
            loading={busy}
            onClick={async () => {
              setBusy(true);
              await onReissue(id.trim());
              setBusy(false);
            }}
          >
            Reemitir
          </Button>
          <Button variant="danger" disabled={!valid || busy} onClick={() => onRevoke(id.trim())}>
            Revocar
          </Button>
        </div>
      </div>
    </Card>
  );
}

export default InvitationsPage;
