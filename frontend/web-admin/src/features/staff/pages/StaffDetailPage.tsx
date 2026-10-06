import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { AlertTriangle, Pencil, Trash2, UserX } from 'lucide-react';
import { INVITABLE_STAFF_ROLES, useCanAny } from '../../../auth';
import { DEMO_ADMIN_PERMISSIONS } from '../../../components/common/navigation';
import { useBreadcrumb } from '../../../components/common';
import { Avatar, Button, Card, EmptyState, ErrorState, LoadingSpinner, Tabs, tabIds, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { clinicsService, errorMessage, licenseStatus, staffService } from '../../../services';
import type { License, StaffMember } from '../../../types';
import { AssignmentModal } from '../components/AssignmentModal';
import { AssignmentsTab } from '../components/AssignmentsTab';
import { DeleteStaffDialog } from '../components/DeleteStaffDialog';
import { HistoryTimeline } from '../components/HistoryTimeline';
import { LicenseModal } from '../components/LicenseModal';
import { LicensesTab } from '../components/LicensesTab';
import { StaffFormModal } from '../components/StaffFormModal';
import { StaffProfileCard } from '../components/StaffProfileCard';
import { StaffStatusBadge } from '../components/StaffStatusBadge';

type Tab = 'asignaciones' | 'licencias' | 'historico';
const UNITS = ['Unidad AMB-07', 'Unidad AMB-12'];

/** Ficha de personal (`/admin/personal/:id`): información, cuenta vinculada, asignaciones, licencias e histórico. */
export function StaffDetailPage() {
  const { id = '' } = useParams();
  const toast = useToast();
  const canUpdate = useCanAny(DEMO_ADMIN_PERMISSIONS);
  const canDelete = useCanAny(DEMO_ADMIN_PERMISSIONS);

  const memberQ = useAsync(() => staffService.get(id), [id]);
  const assignQ = useAsync(() => staffService.listAssignments(id), [id]);
  const licQ = useAsync(() => staffService.listLicenses(id), [id]);
  const histQ = useAsync(() => staffService.listHistory(id), [id]);
  const clinicsQ = useAsync(() => clinicsService.options());
  const member = memberQ.data;
  useBreadcrumb(member?.nombre);

  const [tab, setTab] = useState<Tab>('asignaciones');
  const [editOpen, setEditOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleted, setDeleted] = useState<string | null>(null);
  const [assignOpen, setAssignOpen] = useState(false);
  const [license, setLicense] = useState<{ open: boolean; renewing?: License }>({ open: false });

  const clinics = useMemo(() => clinicsQ.data ?? [], [clinicsQ.data]);
  const licenseAlert = (licQ.data ?? []).filter((l) => licenseStatus(l.vence).status !== 'Vigente');

  if (deleted) {
    return (
      <EmptyState
        icon={Trash2}
        titleAs="h1"
        title="Perfil de personal eliminado"
        description={deleted}
        action={
          <Link to="/admin/personal" className="font-semibold text-primary-600 hover:underline">
            Volver a Personal
          </Link>
        }
      />
    );
  }
  if (memberQ.loading) {
    return (
      <div className="flex justify-center py-24">
        <LoadingSpinner size="lg" label="Cargando perfil…" />
      </div>
    );
  }
  if (memberQ.error || !member) {
    return memberQ.error?.includes('no existe') ? (
      <EmptyState
        icon={UserX}
        titleAs="h1"
        title="Perfil no encontrado"
        description="No existe o fue eliminado."
        action={
          <Link to="/admin/personal" className="font-semibold text-primary-600 hover:underline">
            Volver a Personal
          </Link>
        }
      />
    ) : (
      <ErrorState title="No se pudo cargar el perfil" message={memberQ.error ?? ''} onRetry={memberQ.reload} />
    );
  }

  const clinicName = clinics.find((c) => c.id === member.clinicId)?.nombre ?? member.clinicId;
  const ids = tabIds('staff-detail', tab);
  const accountText =
    member.cuenta.estado === 'vinculada'
      ? `Vinculado a la cuenta ${member.cuenta.userId}. Su acceso se gestiona en Usuarios.`
      : member.cuenta.estado === 'pendiente'
        ? 'Invitación de acceso pendiente. Puedes reenviarla desde Invitaciones.'
        : 'Sin acceso al sistema. Puedes invitarle desde Invitaciones.';

  const remove = async (m: StaffMember, suspendAccount: boolean) => {
    try {
      await staffService.remove(m.id, { suspendAccount });
      setDeleteOpen(false);
      setDeleted(
        `${m.nombre} (${m.id}) ya no aparece en Personal.${suspendAccount ? ' Su cuenta de usuario quedó suspendida.' : ''} Su historial se conserva en Auditoría.`,
      );
    } catch (e) {
      toast({ title: 'No se pudo eliminar', description: errorMessage(e), tone: 'error' });
      setDeleteOpen(false);
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <Link to="/admin/personal" className="w-fit text-[13px] font-medium text-primary-600 hover:underline">
        ← Personal
      </Link>
      <header className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex min-w-0 items-center gap-4">
          <Avatar name={member.nombre} size="lg" />
          <div className="flex min-w-0 flex-col gap-1">
            <div className="flex flex-wrap items-center gap-2.5">
              <h1 className="truncate text-xl font-semibold text-ink sm:text-2xl">{member.nombre}</h1>
              <StaffStatusBadge estado={member.estado} />
              {licenseAlert.length > 0 && (
                <span className="inline-flex items-center gap-1 rounded-full bg-danger-50 px-2.5 py-0.5 text-xs font-semibold text-danger-text">
                  <AlertTriangle size={12} aria-hidden="true" />
                  {licenseAlert.length} {licenseAlert.length === 1 ? 'licencia requiere atención' : 'licencias requieren atención'}
                </span>
              )}
            </div>
            <p className="text-[13px] text-ink-muted">
              <span className="font-mono">{member.id}</span> · {member.puesto} · {clinicName}
            </p>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          {canUpdate && (
            <Button variant="secondary" leftIcon={<Pencil size={16} aria-hidden="true" />} onClick={() => setEditOpen(true)}>
              Editar
            </Button>
          )}
          {canDelete && (
            <Button variant="ghost" leftIcon={<Trash2 size={16} aria-hidden="true" />} onClick={() => setDeleteOpen(true)}>
              Eliminar
            </Button>
          )}
        </div>
      </header>

      <div className="grid grid-cols-1 gap-5 xl:grid-cols-[1fr_2fr]">
        <StaffProfileCard member={member} clinicName={clinicName} />
        <Card padding="none" className="overflow-hidden">
          <div className="border-b border-line px-4 pt-2 sm:px-6">
            <Tabs<Tab>
              label="Secciones del perfil"
              idPrefix="staff-detail"
              value={tab}
              onChange={setTab}
              tabs={[
                { value: 'asignaciones', label: 'Asignaciones', count: assignQ.data?.length },
                { value: 'licencias', label: 'Licencias', count: licQ.data?.length },
                { value: 'historico', label: 'Histórico', count: histQ.data?.length },
              ]}
            />
          </div>
          <div role="tabpanel" id={ids.panel} aria-labelledby={ids.tab} className="p-4 sm:p-6">
            {tab === 'asignaciones' && (
              <AssignmentsTab
                assignments={assignQ.data ?? []}
                loading={assignQ.loading}
                error={assignQ.error}
                onRetry={assignQ.reload}
                canAdd={canUpdate}
                onAdd={() => setAssignOpen(true)}
              />
            )}
            {tab === 'licencias' && (
              <LicensesTab
                licenses={licQ.data ?? []}
                loading={licQ.loading}
                error={licQ.error}
                onRetry={licQ.reload}
                canEdit={canUpdate}
                onAdd={() => setLicense({ open: true })}
                onRenew={(l) => setLicense({ open: true, renewing: l })}
              />
            )}
            {tab === 'historico' && (
              <HistoryTimeline entries={histQ.data ?? []} loading={histQ.loading} error={histQ.error} onRetry={histQ.reload} />
            )}
          </div>
        </Card>
      </div>

      <StaffFormModal
        open={editOpen}
        member={member}
        clinics={clinics}
        roles={INVITABLE_STAFF_ROLES}
        accountText={accountText}
        onClose={() => setEditOpen(false)}
        onSubmit={(input) => staffService.update(member.id, input)}
        onSaved={(m) => {
          setEditOpen(false);
          memberQ.setData(m);
          histQ.reload();
          toast({ title: 'Cambios guardados', description: m.nombre, tone: 'success' });
        }}
      />
      <DeleteStaffDialog member={deleteOpen ? member : null} onConfirm={remove} onCancel={() => setDeleteOpen(false)} />
      <AssignmentModal
        open={assignOpen}
        staffName={member.nombre}
        places={[...clinics.filter((c) => c.estado === 'Activa').map((c) => c.nombre), ...UNITS]}
        onClose={() => setAssignOpen(false)}
        onSubmit={(input) => staffService.addAssignment(member.id, input)}
        onSaved={(a) => {
          setAssignOpen(false);
          assignQ.setData((prev) => [a, ...(prev ?? [])]);
          histQ.reload();
          toast({ title: 'Asignación guardada', description: `${a.lugar} · ${a.turno}`, tone: 'success' });
        }}
      />
      <LicenseModal
        open={license.open}
        staffName={member.nombre}
        renewing={license.renewing}
        onClose={() => setLicense({ open: false })}
        onSubmit={(input, replaceId) => staffService.saveLicense(member.id, input, replaceId)}
        onSaved={(l, renewed) => {
          setLicense({ open: false });
          licQ.setData((prev) => (renewed ? prev?.map((x) => (x.id === l.id ? l : x)) : [...(prev ?? []), l]));
          histQ.reload();
          toast({ title: renewed ? 'Renovación registrada' : 'Licencia añadida', description: l.tipo, tone: 'success' });
        }}
      />
    </div>
  );
}

export default StaffDetailPage;
