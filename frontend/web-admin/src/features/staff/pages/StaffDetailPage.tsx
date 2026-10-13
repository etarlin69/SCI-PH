import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Building2, ChevronLeft, MailPlus, Power, PowerOff } from 'lucide-react';
import { Permissions, useAuth } from '../../../auth';
import { Badge, Button, Card, ConfirmDialog, DetailList, EmptyState, ErrorState, LoadingSpinner, PageHeader, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { formatUtcDateTime } from '../../../utils/dates';
import { describeOrganizationError } from '../../clinics/services/clinicsService';
import { AssignClinicModal } from '../components/AssignClinicModal';
import { InviteStaffModal } from '../components/InviteStaffModal';
import { AccountBadge, STAFF_TYPE_LABELS, StaffStatusBadge, staffFullName } from '../components/StaffBadges';
import { staffService } from '../services/staffService';
import type { ClinicStaffAffiliation } from '../types/staff.types';

/**
 * Ficha del personal (`/admin/personal/:id`): datos, estado, cuenta y afiliaciones. No hay edición ni borrado (el Domain
 * no los contempla). «Invitar» solo aparece sin cuenta vinculada, con personal activo y con Users.Manage; el backend
 * sigue siendo la autoridad.
 */
export function StaffDetailPage() {
  const { id = '' } = useParams();
  const toast = useToast();
  const canInvite = useAuth().can(Permissions.Users.Manage);
  const q = useAsync(() => staffService.get(id), [id]);
  const affiliations = useAsync(() => staffService.listAffiliations(id), [id]);
  const [statusConfirm, setStatusConfirm] = useState(false);
  const [ending, setEnding] = useState<ClinicStaffAffiliation | null>(null);
  const [assigning, setAssigning] = useState(false);
  const [inviting, setInviting] = useState(false);
  const [invitedTo, setInvitedTo] = useState<string | null>(null);
  const staff = q.data;

  const reloadAll = () => {
    q.reload();
    affiliations.reload();
  };

  const toggleStatus = async () => {
    if (!staff) return;
    const deactivating = staff.status === 'Active';
    try {
      if (deactivating) await staffService.deactivate(staff.id);
      else await staffService.activate(staff.id);
      toast({ title: deactivating ? 'Personal desactivado' : 'Personal activado', description: staffFullName(staff), tone: 'success' });
      q.reload();
    } catch (e) {
      toast({ title: 'No se pudo cambiar el estado', description: describeOrganizationError(e), tone: 'error' });
    } finally {
      setStatusConfirm(false);
    }
  };

  const endAffiliation = async () => {
    if (!staff || !ending) return;
    try {
      await staffService.endAffiliation(staff.id, ending.id);
      toast({ title: 'Afiliación finalizada', description: ending.clinicName, tone: 'success' });
      reloadAll();
    } catch (e) {
      toast({ title: 'No se pudo finalizar la afiliación', description: describeOrganizationError(e), tone: 'error' });
    } finally {
      setEnding(null);
    }
  };

  if (q.loading && !staff) {
    return (
      <div className="flex justify-center py-16">
        <LoadingSpinner label="Cargando personal…" />
      </div>
    );
  }

  if (q.error || !staff) {
    return <ErrorState message={q.error ?? 'El personal no existe.'} onRetry={q.reload} />;
  }

  const active = staff.status === 'Active';
  const showInvite = canInvite && !staff.accountLinked && active;
  return (
    <div className="flex flex-col gap-5">
      <Link to="/admin/personal" className="inline-flex w-fit items-center gap-1 text-sm text-ink-3 no-underline hover:text-ink">
        <ChevronLeft size={16} aria-hidden="true" />
        Personal
      </Link>
      <PageHeader
        title={staffFullName(staff)}
        description={STAFF_TYPE_LABELS[staff.type]}
        actions={
          <div className="flex flex-wrap gap-2">
            {showInvite && (
              <Button leftIcon={<MailPlus size={16} aria-hidden="true" />} onClick={() => setInviting(true)}>
                Invitar
              </Button>
            )}
            <Button
              variant={active ? 'danger' : 'secondary'}
              leftIcon={active ? <PowerOff size={16} aria-hidden="true" /> : <Power size={16} aria-hidden="true" />}
              onClick={() => setStatusConfirm(true)}
            >
              {active ? 'Desactivar' : 'Activar'}
            </Button>
          </div>
        }
      />

      {invitedTo && !staff.accountLinked && (
        <p role="status" className="m-0 rounded-lg bg-success-50 px-3 py-2 text-sm text-success-700">
          Invitación enviada a {invitedTo}. La cuenta quedará vinculada cuando se active el enlace.{' '}
          <button type="button" className="font-semibold underline" onClick={() => q.reload()}>
            Actualizar estado
          </button>
        </p>
      )}

      <div className="grid gap-5 xl:grid-cols-2">
        <Card title="Datos del personal" actions={<StaffStatusBadge status={staff.status} />}>
          <DetailList
            columns={2}
            items={[
              { label: 'Tipo', value: STAFF_TYPE_LABELS[staff.type] },
              { label: 'Licencia profesional', value: staff.professionalLicenseNumber ?? '—', mono: Boolean(staff.professionalLicenseNumber) },
              { label: 'Especialidad', value: staff.specialty ?? '—' },
              { label: 'Teléfono', value: staff.phoneNumber ?? '—' },
              {
                label: 'Cuenta de acceso',
                value: (
                  <span className="flex flex-wrap items-center gap-2">
                    <AccountBadge linked={staff.accountLinked} />
                    {staff.accountLinked && staff.identityUserId && canInvite && (
                      <Link to={`/admin/usuarios/${staff.identityUserId}`} className="text-sm font-semibold text-primary-700">
                        Ver cuenta
                      </Link>
                    )}
                  </span>
                ),
              },
              { label: 'Clínicas activas', value: staff.activeClinics.length === 0 ? 'Sin afiliación' : staff.activeClinics.map((c) => c.name).join(', ') },
            ]}
          />
          {!staff.accountLinked && !canInvite && (
            <p className="m-0 mt-3 text-xs text-ink-muted">Invitar a crear la cuenta requiere el permiso Users.Manage.</p>
          )}
        </Card>

        <Card
          title="Afiliaciones a clínicas"
          subtitle="Periodos de trabajo en cada clínica. Al finalizar una afiliación se conserva como historial."
          actions={
            active && (
              <Button size="sm" variant="secondary" leftIcon={<Building2 size={16} aria-hidden="true" />} onClick={() => setAssigning(true)}>
                Asignar clínica
              </Button>
            )
          }
        >
          {affiliations.error ? (
            <ErrorState variant="banner" message={affiliations.error} onRetry={affiliations.reload} />
          ) : affiliations.loading && !affiliations.data ? (
            <div className="flex justify-center py-6">
              <LoadingSpinner label="Cargando afiliaciones…" />
            </div>
          ) : (affiliations.data ?? []).length === 0 ? (
            <EmptyState title="Sin afiliaciones" description="Asigna una clínica para que pueda actuar clínicamente en ella." />
          ) : (
            <ul className="m-0 flex list-none flex-col gap-2 p-0">
              {affiliations.data!.map((a) => (
                <li key={a.id} className="flex flex-col gap-2 rounded-lg border border-line-soft p-3 sm:flex-row sm:items-center sm:justify-between">
                  <span className="flex min-w-0 flex-col gap-1">
                    <span className="flex flex-wrap items-center gap-2 text-sm font-semibold text-ink">
                      {a.clinicName}
                      <Badge tone={a.isActive ? 'success' : 'neutral'}>{a.isActive ? 'Activa' : 'Histórica'}</Badge>
                      {a.clinicStatus === 'Inactive' && <Badge tone="warning">Clínica inactiva</Badge>}
                    </span>
                    <span className="text-xs text-ink-muted">
                      Desde {formatUtcDateTime(a.startedAtUtc)}
                      {a.endedAtUtc && ` · hasta ${formatUtcDateTime(a.endedAtUtc)}`}
                    </span>
                  </span>
                  {a.isActive && (
                    <Button size="sm" variant="secondary" className="shrink-0" onClick={() => setEnding(a)}>
                      Finalizar afiliación
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <AssignClinicModal
        open={assigning}
        staffId={staff.id}
        openClinics={(affiliations.data ?? []).filter((a) => a.isActive).map((a) => ({ id: a.clinicId, name: a.clinicName }))}
        onClose={() => setAssigning(false)}
        onAssigned={() => {
          setAssigning(false);
          toast({ title: 'Clínica asignada', description: staffFullName(staff), tone: 'success' });
          reloadAll();
        }}
      />
      {showInvite && (
        <InviteStaffModal
          staff={staff}
          open={inviting}
          onClose={() => setInviting(false)}
          onInvited={(email) => {
            setInviting(false);
            setInvitedTo(email);
            toast({ title: 'Invitación enviada', description: email, tone: 'success' });
          }}
        />
      )}
      <ConfirmDialog
        open={statusConfirm}
        title={active ? 'Desactivar personal' : 'Activar personal'}
        message={
          active
            ? 'Dejará de poder actuar clínicamente. Sus afiliaciones y su cuenta de acceso se conservan.'
            : 'Volverá a poder actuar clínicamente en las clínicas activas donde tenga afiliación abierta.'
        }
        confirmLabel={active ? 'Desactivar' : 'Activar'}
        tone={active ? 'danger' : 'primary'}
        onConfirm={toggleStatus}
        onCancel={() => setStatusConfirm(false)}
      />
      <ConfirmDialog
        open={ending !== null}
        title="Finalizar afiliación"
        message={`Dejará de poder actuar clínicamente en ${ending?.clinicName ?? 'la clínica'}. La afiliación se conserva como historial.`}
        confirmLabel="Finalizar"
        tone="danger"
        onConfirm={endAffiliation}
        onCancel={() => setEnding(null)}
      />
    </div>
  );
}

export default StaffDetailPage;
