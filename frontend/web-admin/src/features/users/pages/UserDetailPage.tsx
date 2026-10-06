import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { KeyRound, LogOut, Power, PowerOff } from 'lucide-react';
import { useAuth, type SecurityRole } from '../../../auth';
import { Button, Card, ConfirmDialog, DetailList, ErrorState, LoadingSpinner, PageHeader, useToast } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { formatUtcDateTime } from '../../../utils/dates';
import { MfaBadge, RoleList, UserStatusBadges, describeUserAdminError, isLockedOut } from '../components/UserBadges';
import { UserRolesModal } from '../components/UserRolesModal';
import { usersService } from '../services/usersService';

type Pending = 'activate' | 'deactivate' | 'revoke' | null;

const CONFIRM: Record<Exclude<Pending, null>, { title: string; message: string; label: string; tone: 'danger' | 'primary' }> = {
  deactivate: {
    title: 'Desactivar cuenta',
    message: 'La cuenta no podrá iniciar sesión y se cerrarán sus sesiones abiertas. Podrás reactivarla más tarde.',
    label: 'Desactivar',
    tone: 'danger',
  },
  activate: { title: 'Reactivar cuenta', message: 'La cuenta podrá volver a iniciar sesión.', label: 'Reactivar', tone: 'primary' },
  revoke: {
    title: 'Cerrar todas las sesiones',
    message: 'Se invalidarán las sesiones abiertas de esta cuenta en todos los dispositivos.',
    label: 'Cerrar sesiones',
    tone: 'danger',
  },
};

/** Detalle de una cuenta (`/admin/usuarios/:id`): estado, roles y sesiones. */
export function UserDetailPage() {
  const { id = '' } = useParams();
  const { user: me } = useAuth();
  const toast = useToast();
  const q = useAsync(() => usersService.get(id), [id]);
  const [pending, setPending] = useState<Pending>(null);
  const [rolesOpen, setRolesOpen] = useState(false);
  const [savingRoles, setSavingRoles] = useState(false);
  const [rolesError, setRolesError] = useState<string | null>(null);

  const user = q.data;
  const isSelf = !!user && user.id === me?.id;

  const runAction = async () => {
    if (!user || !pending) return;
    try {
      if (pending === 'activate') await usersService.activate(user.id);
      else if (pending === 'deactivate') await usersService.deactivate(user.id);
      else await usersService.revokeSessions(user.id);
      toast({ title: CONFIRM[pending].title, description: user.userName, tone: 'success' });
      q.reload();
    } catch (e) {
      toast({ title: 'No se pudo completar la acción', description: describeUserAdminError(e), tone: 'error' });
    } finally {
      setPending(null);
    }
  };

  const saveRoles = async (roles: SecurityRole[]) => {
    if (!user) return;
    setSavingRoles(true);
    setRolesError(null);
    try {
      await usersService.setRoles(user.id, roles);
      toast({ title: 'Roles actualizados', description: user.userName, tone: 'success' });
      setRolesOpen(false);
      q.reload();
    } catch (e) {
      setRolesError(describeUserAdminError(e));
    } finally {
      setSavingRoles(false);
    }
  };

  if (q.loading && !user) {
    return (
      <div className="flex justify-center py-16">
        <LoadingSpinner label="Cargando cuenta…" />
      </div>
    );
  }
  if (q.error || !user) {
    return (
      <div className="flex flex-col gap-5">
        <PageHeader title="Cuenta" back={{ to: '/admin/usuarios', label: 'Usuarios' }} />
        <ErrorState message={q.error ?? 'La cuenta no existe.'} onRetry={q.reload} />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title={user.userName}
        description={user.email ?? undefined}
        back={{ to: '/admin/usuarios', label: 'Usuarios' }}
        meta={<UserStatusBadges user={user} />}
        actions={
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" leftIcon={<KeyRound size={16} aria-hidden="true" />} onClick={() => setRolesOpen(true)}>
              Roles
            </Button>
            <Button variant="secondary" leftIcon={<LogOut size={16} aria-hidden="true" />} onClick={() => setPending('revoke')}>
              Cerrar sesiones
            </Button>
            {user.isActive ? (
              <Button variant="danger" leftIcon={<PowerOff size={16} aria-hidden="true" />} onClick={() => setPending('deactivate')}>
                Desactivar
              </Button>
            ) : (
              <Button leftIcon={<Power size={16} aria-hidden="true" />} onClick={() => setPending('activate')}>
                Reactivar
              </Button>
            )}
          </div>
        }
      />

      {isSelf && (
        <p role="note" className="rounded-lg border border-warning-500/40 bg-warning-50 px-3.5 py-2.5 text-[13px] text-warning-700">
          Es tu propia cuenta: cualquier cambio cerrará también tu sesión actual.
        </p>
      )}

      <Card title="Cuenta">
        <DetailList
          items={[
            { label: 'Usuario', value: user.userName },
            { label: 'Email', value: user.email ?? '—' },
            { label: 'Email confirmado', value: user.emailConfirmed ? 'Sí' : 'No' },
            { label: 'Verificación en dos pasos', value: <MfaBadge enabled={user.twoFactorEnabled} /> },
            { label: 'Roles', value: <RoleList roles={user.roles} /> },
            {
              label: 'Bloqueo por intentos fallidos',
              value: isLockedOut(user) ? `Hasta ${formatUtcDateTime(user.lockedOutUntilUtc)}` : 'No',
            },
            { label: 'Creada', value: formatUtcDateTime(user.createdAtUtc) },
            { label: 'Último acceso', value: formatUtcDateTime(user.lastLoginAtUtc) },
            { label: 'Id', value: user.id, mono: true },
          ]}
        />
      </Card>

      <UserRolesModal
        open={rolesOpen}
        userName={user.userName}
        currentRoles={user.roles}
        saving={savingRoles}
        error={rolesError}
        onSave={(roles) => void saveRoles(roles)}
        onClose={() => {
          setRolesOpen(false);
          setRolesError(null);
        }}
      />
      {pending && (
        <ConfirmDialog
          open
          title={CONFIRM[pending].title}
          message={CONFIRM[pending].message}
          confirmLabel={CONFIRM[pending].label}
          tone={CONFIRM[pending].tone}
          onConfirm={runAction}
          onCancel={() => setPending(null)}
        />
      )}
    </div>
  );
}

export default UserDetailPage;
